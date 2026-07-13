using System.Text;
using System.Text.Json;
using NotifyRelay.Data.Contracts;
using NotifyRelay.Data.Models;
using NotifyRelay.Native;

namespace NotifyRelay.Services;

public class HeartbeatProcessor
{
    private const string HeartbeatTcpPrefix = "HEARTBEAT_TCP:";

    private readonly ILogger _logger;
    private readonly IDeviceManager _deviceManager;

    public HeartbeatProcessor(
        ILogger logger,
        IDeviceManager deviceManager)
    {
        _logger = logger;
        _deviceManager = deviceManager;
    }

    public bool TryProcessHeartbeat(string message, PairedDevice? device, Action<PairedDevice>? markDeviceAlive)
    {
        var json = message.StartsWith(HeartbeatTcpPrefix)
            ? NativeCore.ParseHeartbeatTcpJson(message)
            : NativeCore.ParseHeartbeatJson(message);
        if (json == null) return false;

        var fields = ParseHeartbeatJson(json);
        if (fields == null) return false;

        var targetDevice = device?.Id == fields.DeviceId
            ? device
            : _deviceManager.FindDeviceById(fields.DeviceId);

        if (targetDevice == null) return false;

        try
        {
            if (fields.BatteryLevel >= 0)
            {
                _deviceManager.UpdateDeviceStatus(targetDevice, new DeviceStatus
                {
                    BatteryStatus = fields.BatteryLevel,
                    ChargingStatus = fields.IsCharging
                });
            }

            markDeviceAlive?.Invoke(targetDevice);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "处理心跳包失败");
            return false;
        }
    }

    public UdpHeartbeatInfo? ParseUdpHeartbeat(string message)
    {
        var json = NativeCore.ParseHeartbeatJson(message);
        if (json == null) return null;

        return ParseHeartbeatJson(json);
    }

    public void UpdateDeviceFromUdp(string deviceId, string message, Action<PairedDevice>? markDeviceAlive)
    {
        var device = _deviceManager.FindDeviceById(deviceId);
        if (device == null) return;

        if (!string.IsNullOrEmpty(message))
        {
            var json = message.StartsWith(HeartbeatTcpPrefix)
                ? NativeCore.ParseHeartbeatTcpJson(message)
                : NativeCore.ParseHeartbeatJson(message);
            if (json != null)
            {
                var fields = ParseHeartbeatJson(json);
                if (fields != null && fields.BatteryLevel >= 0)
                {
                    _deviceManager.UpdateDeviceStatus(device, new DeviceStatus
                    {
                        BatteryStatus = fields.BatteryLevel,
                        ChargingStatus = fields.IsCharging
                    });
                }
            }
        }

        markDeviceAlive?.Invoke(device);
    }

    private static UdpHeartbeatInfo? ParseHeartbeatJson(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            var nameB64 = root.GetProperty("name_b64").GetString() ?? string.Empty;
            string decodedName;
            try
            {
                decodedName = Encoding.UTF8.GetString(Convert.FromBase64String(nameB64));
            }
            catch
            {
                decodedName = nameB64;
            }

            var battery = root.GetProperty("battery").GetInt32();

            return new UdpHeartbeatInfo
            {
                DeviceId = root.GetProperty("uuid").GetString() ?? string.Empty,
                DeviceName = decodedName,
                Port = root.GetProperty("port").GetInt32(),
                BatteryLevel = battery < 0 ? Math.Abs(battery) : battery,
                IsCharging = battery >= 0,
                DeviceType = root.GetProperty("device_type").GetString() ?? "unknown"
            };
        }
        catch
        {
            return null;
        }
    }
}

public class UdpHeartbeatInfo
{
    public string DeviceId { get; set; } = string.Empty;
    public string DeviceName { get; set; } = string.Empty;
    public int Port { get; set; }
    public int BatteryLevel { get; set; }
    public bool IsCharging { get; set; }
    public string DeviceType { get; set; } = string.Empty;
}
