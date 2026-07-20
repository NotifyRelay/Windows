using CommunityToolkit.WinUI;
using MeaMod.DNS.Multicast;
using Microsoft.UI.Dispatching;
using NotifyRelay.Data.AppDatabase.Models;
using NotifyRelay.Data.Contracts;
using NotifyRelay.Data.Enums;
using NotifyRelay.Data.EventArguments;
using NotifyRelay.Data.Models;
using NotifyRelay.Native;
using NotifyRelay.Services.Socket;

namespace NotifyRelay.Services;

public class DiscoveryService(
    ILogger logger,
    IMdnsService mdnsService,
    IDeviceManager deviceManager,
    HeartbeatProcessor heartbeatProcessor,
    Func<INetworkService> networkServiceFactory
    ) : IDiscoveryService
{
    private readonly DispatcherQueue dispatcher = DispatcherQueue.GetForCurrentThread();
    private LocalDeviceEntity? localDevice;
    private bool isInitialized = false;

    public ObservableCollection<DiscoveredDevice> DiscoveredDevices { get; } = [];
    public List<DiscoveredMdnsServiceArgs> DiscoveredMdnsServices { get; } = [];

    public async Task StartDiscoveryAsync()
    {
        try
        {
            await dispatcher.EnqueueAsync(() =>
            {
                DiscoveredDevices.Clear();
                DiscoveredMdnsServices.Clear();
                logger.LogInformation("设备列表已清理");
            });

            localDevice = await deviceManager.GetLocalDeviceAsync();
            logger.LogInformation("本地设备初始化完成：{deviceId}, {deviceName}", localDevice.DeviceId, localDevice.DeviceName);

            mdnsService.DiscoveredMdnsService += OnDiscoveredMdnsService;
            mdnsService.ServiceInstanceShutdown += OnServiceInstanceShutdown;
            deviceManager.LocalDeviceNameChanged += OnLocalDeviceNameChanged;
            logger.LogInformation("事件处理程序已设置");

            var networkService = networkServiceFactory();
            var serverPort = networkService.ServerPort == 0 ? 23333 : networkService.ServerPort;
            var udpBroadcast = new UdpBroadcast
            {
                DeviceId = localDevice.DeviceId,
                DeviceName = localDevice.DeviceName,
                PublicKey = NativeCore.GetPublicKey() ?? string.Empty,
                Port = serverPort,
                TimeStamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
            };
            mdnsService.AdvertiseService(udpBroadcast, serverPort);
            logger.LogInformation("mDNS服务广告已发布");

            // 通过 Rust 内核启动周期性设备广播
            var systemInfoService = Ioc.Default.GetService<ISystemInfoService>();
            var batteryLevel = systemInfoService?.GetSystemBatteryLevel() ?? 100;
            var isCharging = systemInfoService?.GetSystemChargingStatus() ?? true;
            var signedBattery = isCharging ? Math.Abs(batteryLevel) : -Math.Abs(batteryLevel);
            NativeCore.PeriodicBroadcast(1, localDevice.DeviceId, localDevice.DeviceName, signedBattery, "pc");

            // 订阅心跳处理器发现事件
            heartbeatProcessor.DeviceDiscovered += OnDeviceDiscovered;

            isInitialized = true;
            logger.LogInformation("发现服务已完全初始化");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "启动发现服务时出错");
            isInitialized = false;
            await dispatcher.EnqueueAsync(() =>
            {
                DiscoveredDevices.Clear();
                DiscoveredMdnsServices.Clear();
            });
        }
    }

    private void OnLocalDeviceNameChanged(object? sender, string newName)
    {
        try
        {
            if (localDevice == null) return;
            logger.LogInformation("本地设备名已更改：{newName}", newName);
            localDevice.DeviceName = newName;
            NativeCore.PeriodicBroadcast(2, name: newName);

            mdnsService.UnAdvertiseService();
            var networkService = networkServiceFactory();
            var serverPort = networkService.ServerPort == 0 ? 23333 : networkService.ServerPort;
            var udpBroadcast = new UdpBroadcast
            {
                DeviceId = localDevice.DeviceId,
                DeviceName = newName,
                PublicKey = NativeCore.GetPublicKey() ?? string.Empty,
                Port = serverPort,
                TimeStamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
            };
            mdnsService.AdvertiseService(udpBroadcast, serverPort);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "处理本地设备名更改时出错");
        }
    }

    private async void OnDeviceDiscovered(string uuid, string? name, ushort port, int battery, string deviceType)
    {
        if (uuid == localDevice?.DeviceId) return;

        await dispatcher.EnqueueAsync(() =>
        {
            if (!isInitialized) return;

            var discovered = new DiscoveredDevice(
                uuid, null, name ?? "unknown",
                DateTimeOffset.UtcNow, DeviceOrigin.UdpBroadcast, port);

            var existing = DiscoveredDevices.FirstOrDefault(d => d.DeviceId == uuid);
            if (existing is not null)
            {
                var index = DiscoveredDevices.IndexOf(existing);
                DiscoveredDevices[index] = discovered;
            }
            else
            {
                DiscoveredDevices.Add(discovered);
            }
        });
    }

    private async void OnDiscoveredMdnsService(object? sender, DiscoveredMdnsServiceArgs service)
    {
        if (service.DeviceId == localDevice?.DeviceId) return;
        if (DiscoveredMdnsServices.Any(s => s.DeviceId == service.DeviceId)) return;

        DiscoveredMdnsServices.Add(service);
        logger.LogInformation("发现服务实例：{deviceId}，{deviceName}", service.DeviceId, service.DeviceName);

        var device = new DiscoveredDevice(
            service.DeviceId, null, service.DeviceName,
            DateTimeOffset.UtcNow, DeviceOrigin.MdnsService, 23333);

        await dispatcher.EnqueueAsync(() =>
        {
            if (!isInitialized) return;
            if (device.DeviceId == localDevice?.DeviceId) return;

            var existing = DiscoveredDevices.FirstOrDefault(d => d.DeviceId == device.DeviceId);
            if (existing is not null)
            {
                var index = DiscoveredDevices.IndexOf(existing);
                DiscoveredDevices[index] = device;
            }
            else
            {
                DiscoveredDevices.Add(device);
            }
        });
    }

    private async void OnServiceInstanceShutdown(object? sender, ServiceInstanceShutdownEventArgs e)
    {
        var deviceId = e.ServiceInstanceName.ToString().Split('.')[0];

        await dispatcher.EnqueueAsync(() =>
        {
            DiscoveredMdnsServices.RemoveAll(s => s.DeviceId == deviceId);
            try
            {
                var deviceToRemove = DiscoveredDevices
                    .Where(d => d.Origin is DeviceOrigin.MdnsService)
                    .FirstOrDefault(d => d.DeviceId == deviceId);
                if (deviceToRemove is not null)
                    DiscoveredDevices.Remove(deviceToRemove);
            }
            catch (Exception ex) when (ex is ArgumentOutOfRangeException or InvalidOperationException)
            {
                logger.LogWarning("移除设备时：{Message}", ex.Message);
            }
            catch (Exception ex)
            {
                logger.LogWarning("移除设备时出现意外错误：{Message}", ex.Message);
            }
        });
    }

    public void StopDiscovery()
    {
        NativeCore.PeriodicBroadcast(0);
        heartbeatProcessor.DeviceDiscovered -= OnDeviceDiscovered;

        try
        {
            mdnsService.UnAdvertiseService();
            deviceManager.LocalDeviceNameChanged -= OnLocalDeviceNameChanged;
            dispatcher.TryEnqueue(() =>
            {
                DiscoveredDevices.Clear();
                DiscoveredMdnsServices.Clear();
                isInitialized = false;
            });
        }
        catch (Exception ex)
        {
            logger.LogError("停止发现服务时出错：{message}", ex.Message);
        }
    }
}
