using System.Text;
using NotifyRelay.Data.Contracts;
using NotifyRelay.Data.Models;

namespace NotifyRelay.Services;

/// <summary>
/// 心跳处理器
///
/// 职责：
/// - 统一解析 TCP/UDP 心跳消息格式
/// - 更新设备在线状态和电量信息
/// - 消除 NetworkService 和 DiscoveryService 中的重复心跳解析代码
///
/// 心跳格式：<uuid>:<displayName(base64)>:<port>:<+/-><batteryLevel>:<deviceType>
/// </summary>
public class HeartbeatProcessor
{
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
    /// 尝试作为 TCP 心跳处理消息
    /// </summary>
    /// <param name="message">原始消息</param>
    /// <param name="device">当前设备（如果已绑定会话）</param>
    /// <param name="markDeviceAlive">设备活跃回调</param>
    /// <returns>如果消息是心跳格式并成功处理则返回 true</returns>
    public bool TryProcessHeartbeat(string message, PairedDevice? device, Action<PairedDevice>? markDeviceAlive)
    {
        var parts = message.Split(':');
        if (parts.Length < 5) return false;

        var heartbeatDeviceId = parts[0];

        var targetDevice = device?.Id == heartbeatDeviceId
            ? device
            : _deviceManager.FindDeviceById(heartbeatDeviceId);

        if (targetDevice == null) return false;

        try
        {
            var deviceStatus = ParseBatteryStatus(parts[3]);
            if (deviceStatus != null)
            {
                _deviceManager.UpdateDeviceStatus(targetDevice, deviceStatus);
            }

            markDeviceAlive?.Invoke(targetDevice);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "解析心跳包失败");
            return false;
        }
    }

    /// <summary>
    /// 解析 UDP/UDP 广播心跳消息，获取设备基本信息和电量
    /// </summary>
    public UdpHeartbeatInfo? ParseUdpHeartbeat(string message)
    {
        var parts = message.Split(':');
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

        int batteryLevel = 0;
        bool isCharging = false;
        try
        {
            var batteryPart = parts[3];
            isCharging = batteryPart[0] == '+';
            batteryLevel = int.TryParse(batteryPart[1..], out var parsed)
                ? Math.Clamp(parsed, 0, 100) : 0;
        }
        catch
        {
            // 电量解析失败，使用默认值
        }

        var deviceType = parts.Length > 4 ? parts[4] : "unknown";

        return new UdpHeartbeatInfo
        {
            DeviceId = parts[0],
            DeviceName = decodedName,
            Port = port,
            BatteryLevel = batteryLevel,
            IsCharging = isCharging,
            DeviceType = deviceType
        };
    }

    /// <summary>
    /// 更新已配对设备的 UDP 心跳状态（电量 + 活跃标记）
    /// </summary>
    public void UpdateDeviceFromUdp(string deviceId, string message, Action<PairedDevice>? markDeviceAlive)
    {
        var device = _deviceManager.FindDeviceById(deviceId);
        if (device == null) return;

        if (!string.IsNullOrEmpty(message))
        {
            var deviceStatus = ParseBatteryFromMessage(message);
            if (deviceStatus != null)
            {
                _deviceManager.UpdateDeviceStatus(device, deviceStatus);
            }
        }

        markDeviceAlive?.Invoke(device);
    }

    private DeviceStatus? ParseBatteryFromMessage(string message)
    {
        var parts = message.Split(':');
        if (parts.Length < 5) return null;

        return ParseBatteryStatus(parts[3]);
    }

    private static DeviceStatus? ParseBatteryStatus(string batteryPart)
    {
        if (string.IsNullOrEmpty(batteryPart) || batteryPart.Length < 2) return null;

        var chargeSign = batteryPart[0];
        var isCharging = chargeSign == '+';
        var batteryLevelStr = batteryPart[1..];
        var batteryLevel = int.TryParse(batteryLevelStr, out var parsedBattery)
            ? Math.Clamp(parsedBattery, 0, 100) : 0;

        return new DeviceStatus
        {
            BatteryStatus = batteryLevel,
            ChargingStatus = isCharging
        };
    }
}

/// <summary>
/// UDP 心跳解析结果
/// </summary>
public class UdpHeartbeatInfo
{
    public string DeviceId { get; set; } = string.Empty;
    public string DeviceName { get; set; } = string.Empty;
    public int Port { get; set; }
    public int BatteryLevel { get; set; }
    public bool IsCharging { get; set; }
    public string DeviceType { get; set; } = string.Empty;
}
