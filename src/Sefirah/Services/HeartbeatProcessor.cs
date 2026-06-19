using System.Text;
using NotifyRelay.Data.Contracts;
using NotifyRelay.Data.Models;

namespace NotifyRelay.Services;

/// <summary>
/// 心跳处理器
///
/// 职责：
/// - 统一解析 TCP/UDP 心跳消息格式（TCP 带 HEARTBEAT_TCP: 前缀，UDP 不带）
/// - 更新设备在线状态和电量信息
/// - 消除 NetworkService 和 DiscoveryService 中的重复心跳解析代码
///
/// TCP 心跳格式：HEARTBEAT_TCP:<uuid>:<displayName(base64)>:<port>:<+/-><batteryLevel>:<deviceType>
/// UDP 心跳格式：<uuid>:<displayName(base64)>:<port>:<+/-><batteryLevel>:<deviceType>
/// </summary>
public class HeartbeatProcessor
{
    private const string HeartbeatTcpPrefix = "HEARTBEAT_TCP:";

    private readonly ILogger<HeartbeatProcessor> _logger;
    private readonly IDeviceManager _deviceManager;

    public HeartbeatProcessor(
        ILogger<HeartbeatProcessor> logger,
        IDeviceManager deviceManager)
    {
        _logger = logger;
        _deviceManager = deviceManager;
    }

    /// <summary>
    /// 提取心跳载荷（去掉 HEARTBEAT_TCP: 前缀，如果有）
    /// </summary>
    private static string GetHeartbeatPayload(string message)
    {
        return message.StartsWith(HeartbeatTcpPrefix)
            ? message[HeartbeatTcpPrefix.Length..]
            : message;
    }

    /// <summary>
    /// 尝试处理 TCP 心跳消息（支持 HEARTBEAT_TCP: 开头或无前缀两种格式）
    /// </summary>
    public bool TryProcessHeartbeat(string message, PairedDevice? device, Action<PairedDevice>? markDeviceAlive)
    {
        var payload = GetHeartbeatPayload(message);
        var fields = ParseHeartbeatFields(payload);
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

    /// <summary>
    /// 解析 UDP 广播心跳消息
    /// </summary>
    public UdpHeartbeatInfo? ParseUdpHeartbeat(string message)
    {
        var fields = ParseHeartbeatFields(message);
        if (fields == null) return null;

        return new UdpHeartbeatInfo
        {
            DeviceId = fields.DeviceId,
            DeviceName = fields.DeviceName,
            Port = fields.Port,
            BatteryLevel = fields.BatteryLevel,
            IsCharging = fields.IsCharging,
            DeviceType = fields.DeviceType
        };
    }

    /// <summary>
    /// 通过 UDP 心跳更新已配对设备状态
    /// </summary>
    public void UpdateDeviceFromUdp(string deviceId, string message, Action<PairedDevice>? markDeviceAlive)
    {
        var device = _deviceManager.FindDeviceById(deviceId);
        if (device == null) return;

        if (!string.IsNullOrEmpty(message))
        {
            var fields = ParseHeartbeatFields(GetHeartbeatPayload(message));
            if (fields != null && fields.BatteryLevel >= 0)
            {
                _deviceManager.UpdateDeviceStatus(device, new DeviceStatus
                {
                    BatteryStatus = fields.BatteryLevel,
                    ChargingStatus = fields.IsCharging
                });
            }
        }

        markDeviceAlive?.Invoke(device);
    }

    /// <summary>
    /// 统一的心跳字段解析（TCP 和 UDP 共用）
    /// 格式：<uuid>:<displayName(base64)>:<port>:<+/-><batteryLevel>:<deviceType>
    /// </summary>
    private static HeartbeatFields? ParseHeartbeatFields(string payload)
    {
        var parts = payload.Split(':');
        if (parts.Length < 5) return null;

        string decodedName;
        try
        {
            decodedName = Encoding.UTF8.GetString(Convert.FromBase64String(parts[1]));
        }
        catch
        {
            decodedName = parts[1];
        }

        var port = int.TryParse(parts[2], out var parsedPort) ? parsedPort : 23333;

        int batteryLevel = -1;
        bool isCharging = false;
        try
        {
            var batteryPart = parts[3];
            if (batteryPart.Length >= 2)
            {
                isCharging = batteryPart[0] == '+';
                batteryLevel = int.TryParse(batteryPart[1..], out var parsed)
                    ? Math.Clamp(parsed, 0, 100) : -1;
            }
        }
        catch
        {
            // 电量解析失败，使用默认值
        }

        var deviceType = parts.Length > 4 ? parts[4] : "unknown";

        return new HeartbeatFields
        {
            DeviceId = parts[0],
            DeviceName = decodedName,
            Port = port,
            BatteryLevel = batteryLevel,
            IsCharging = isCharging,
            DeviceType = deviceType
        };
    }

    private sealed class HeartbeatFields
    {
        public string DeviceId { get; set; } = string.Empty;
        public string DeviceName { get; set; } = string.Empty;
        public int Port { get; set; }
        public int BatteryLevel { get; set; } = -1;
        public bool IsCharging { get; set; }
        public string DeviceType { get; set; } = string.Empty;
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
