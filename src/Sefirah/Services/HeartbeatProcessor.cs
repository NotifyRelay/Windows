using NotifyRelay.Data.Contracts;
using NotifyRelay.Data.Models;
using NotifyRelay.Native;

namespace NotifyRelay.Services;

public class HeartbeatProcessor
{
    private readonly ILogger _logger;
    private readonly IDeviceManager _deviceManager;

    public event Action<string, string?, ushort, int, string>? DeviceDiscovered;

    public HeartbeatProcessor(
        ILogger logger,
        IDeviceManager deviceManager)
    {
        _logger = logger;
        _deviceManager = deviceManager;
    }

    public void HandleUdpHeartbeat(string uuid, string? name, ushort port, int battery, string deviceType)
    {
        DeviceDiscovered?.Invoke(uuid, name, port, battery, deviceType);

        // 记录到 Rust core 的发现状态中
        var ip = NativeCore.GetLocalIp() ?? "0.0.0.0";
        NativeCore.RecordDiscoveredDevice(uuid, name, ip, port, battery, deviceType);

        var targetDevice = _deviceManager.FindDeviceById(uuid);
        if (targetDevice == null) return;

        try
        {
            if (!string.IsNullOrEmpty(name) && name != "unknown")
            {
                targetDevice.Name = name;
                _deviceManager.SaveDevice(targetDevice);
            }
            var absBattery = battery < 0 ? Math.Abs(battery) : battery;
            var isCharging = battery >= 0;
            if (absBattery >= 0)
            {
                _deviceManager.UpdateDeviceStatus(targetDevice, new DeviceStatus
                {
                    BatteryStatus = absBattery,
                    ChargingStatus = isCharging
                });
            }
            MarkDeviceAlive(targetDevice);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "处理 UDP 心跳包失败");
        }
    }

    private void MarkDeviceAlive(PairedDevice device)
    {
        device.LastHeartbeat = DateTime.UtcNow;
        if (!device.ConnectionStatus)
        {
            App.MainWindow?.DispatcherQueue?.TryEnqueue(() =>
            {
                device.ConnectionStatus = true;
            });
        }
    }
}


