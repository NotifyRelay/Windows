using CommunityToolkit.WinUI;
using Microsoft.UI.Dispatching;
using NotifyRelay.Data.AppDatabase.Models;
using NotifyRelay.Data.Contracts;
using NotifyRelay.Data.Enums;
using NotifyRelay.Data.Models;
using NotifyRelay.Native;

namespace NotifyRelay.Services;

public class DiscoveryService(
    ILogger logger,
    IDeviceManager deviceManager,
    HeartbeatProcessor heartbeatProcessor,
    Func<INetworkService> networkServiceFactory
    ) : IDiscoveryService
{
    private readonly DispatcherQueue dispatcher = DispatcherQueue.GetForCurrentThread();
    private LocalDeviceEntity? localDevice;
    private bool isInitialized = false;

    public ObservableCollection<DiscoveredDevice> DiscoveredDevices { get; } = [];

    public async Task StartDiscoveryAsync()
    {
        try
        {
            await dispatcher.EnqueueAsync(() =>
            {
                DiscoveredDevices.Clear();
                logger.LogInformation("设备列表已清理");
            });

            localDevice = await deviceManager.GetLocalDeviceAsync();
            logger.LogInformation("本地设备初始化完成：{deviceId}, {deviceName}", localDevice.DeviceId, localDevice.DeviceName);

            deviceManager.LocalDeviceNameChanged += OnLocalDeviceNameChanged;
            logger.LogInformation("事件处理程序已设置");

            var networkService = networkServiceFactory();
            var serverPort = networkService.ServerPort == 0 ? 23333 : networkService.ServerPort;
            NativeCore.StartMdnsAdvertiser(localDevice.DeviceId, localDevice.DeviceName, (ushort)serverPort, NativeCore.GetPublicKey() ?? string.Empty, "pc");
            NativeCore.StartMdnsDiscovery();
            logger.LogInformation("Rust mDNS 服务已启动");

            // 通过 Rust 内核启动周期性设备广播
            var systemInfoService = Ioc.Default.GetService<ISystemInfoService>();
            var batteryLevel = systemInfoService?.GetSystemBatteryLevel() ?? 100;
            var isCharging = systemInfoService?.GetSystemChargingStatus() ?? true;
            var signedBattery = isCharging ? Math.Abs(batteryLevel) : -Math.Abs(batteryLevel);
            NativeCore.PeriodicBroadcast(1, localDevice.DeviceId, localDevice.DeviceName, signedBattery, "pc");

            // 订阅心跳处理器发现事件和 mDNS 发现事件
            heartbeatProcessor.DeviceDiscovered += OnDeviceDiscovered;
            heartbeatProcessor.MdnsDeviceDiscovered += OnMdnsDeviceDiscovered;

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

            var networkService = networkServiceFactory();
            var serverPort = networkService.ServerPort == 0 ? 23333 : networkService.ServerPort;
            NativeCore.StopMdnsAdvertiser();
            NativeCore.StartMdnsAdvertiser(localDevice.DeviceId, newName, (ushort)serverPort, NativeCore.GetPublicKey() ?? string.Empty, "pc");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "处理本地设备名更改时出错");
        }
    }

    private async void OnDeviceDiscovered(string uuid, string? name, ushort port, int battery, string deviceType, string? ip)
    {
        if (uuid == localDevice?.DeviceId) return;

        await dispatcher.EnqueueAsync(() =>
        {
            if (!isInitialized) return;

            var discovered = new DiscoveredDevice(
                uuid, ip, name ?? "unknown",
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

    private async void OnMdnsDeviceDiscovered(string uuid, string? name, string ip, ushort port, string deviceType)
    {
        if (uuid == localDevice?.DeviceId) return;

        logger.LogInformation("mDNS 发现设备：{deviceId}，{deviceName}，{ip}", uuid, name, ip);

        await dispatcher.EnqueueAsync(() =>
        {
            if (!isInitialized) return;

            var discovered = new DiscoveredDevice(
                uuid, ip, name ?? "unknown",
                DateTimeOffset.UtcNow, DeviceOrigin.MdnsService, port);

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

    public void StopDiscovery()
    {
        NativeCore.PeriodicBroadcast(0);
        heartbeatProcessor.DeviceDiscovered -= OnDeviceDiscovered;
        heartbeatProcessor.MdnsDeviceDiscovered -= OnMdnsDeviceDiscovered;

        try
        {
            NativeCore.StopMdnsAdvertiser();
            NativeCore.StopMdnsDiscovery();
            deviceManager.LocalDeviceNameChanged -= OnLocalDeviceNameChanged;
            dispatcher.TryEnqueue(() =>
            {
                DiscoveredDevices.Clear();
                isInitialized = false;
            });
        }
        catch (Exception ex)
        {
            logger.LogError("停止发现服务时出错：{message}", ex.Message);
        }
    }
}
