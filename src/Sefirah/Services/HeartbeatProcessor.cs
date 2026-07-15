using System.Runtime.InteropServices;
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

    /// <summary>
    /// 由 Rust on_heartbeat_udp 回调直接调用的入口（带结构化参数，无中间 JSON）
    /// </summary>
    public void HandleUdpHeartbeat(string uuid, string? nameB64, ushort port, int battery, string deviceType)
    {
        var displayName = TryDecodeName(nameB64);
        var targetDevice = _deviceManager.FindDeviceById(uuid);
        if (targetDevice == null) return;

        try
        {
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

    public bool TryProcessHeartbeat(string message, PairedDevice? device, Action<PairedDevice>? markDeviceAlive)
    {
        var result = ParseUdpHeartbeatDirect(message);
        if (result == null) return false;

        var targetDevice = device?.Id == result.DeviceId
            ? device
            : _deviceManager.FindDeviceById(result.DeviceId);

        if (targetDevice == null) return false;

        try
        {
            if (result.BatteryLevel >= 0)
            {
                _deviceManager.UpdateDeviceStatus(targetDevice, new DeviceStatus
                {
                    BatteryStatus = result.BatteryLevel,
                    ChargingStatus = result.IsCharging
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
        return ParseUdpHeartbeatDirect(message);
    }

    public void UpdateDeviceFromUdp(string deviceId, string message, Action<PairedDevice>? markDeviceAlive)
    {
        var device = _deviceManager.FindDeviceById(deviceId);
        if (device == null) return;

        if (!string.IsNullOrEmpty(message))
        {
            var fields = ParseUdpHeartbeatDirect(message);
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
    /// 通过 Rust 解析原始协议行并直接构造结构化数据（消除 JSON 二次解析）
    /// </summary>
    private UdpHeartbeatInfo? ParseUdpHeartbeatDirect(string message)
    {
        var isTcp = message.StartsWith(HeartbeatTcpPrefix);
        var result = new UdpHeartbeatBuilder();
        if (isTcp)
        {
            NotifyRelayCore.OnHeartbeatTcpWithCb cb = (uuidPtr, nameB64Ptr, port, battery, deviceTypePtr, ipPtr, _) =>
            {
                var uuid = Marshal.PtrToStringUTF8(uuidPtr);
                var nameB64 = Marshal.PtrToStringUTF8(nameB64Ptr);
                var deviceType = Marshal.PtrToStringUTF8(deviceTypePtr) ?? "unknown";
                if (uuid != null)
                {
                    result.DeviceId = uuid;
                    result.DeviceName = TryDecodeName(nameB64);
                    result.Port = port;
                    result.BatteryLevel = battery < 0 ? Math.Abs(battery) : battery;
                    result.IsCharging = battery >= 0;
                    result.DeviceType = deviceType;
                    result.HasValue = true;
                }
            };
            NativeCore.ParseHeartbeatTcpWithCb(message, cb, IntPtr.Zero);
        }
        else
        {
            NativeCore.ParseHeartbeatWithCb(message, (uuidPtr, nameB64Ptr, port, battery, deviceTypePtr, _) =>
            {
                var uuid = Marshal.PtrToStringUTF8(uuidPtr);
                var nameB64 = Marshal.PtrToStringUTF8(nameB64Ptr);
                var deviceType = Marshal.PtrToStringUTF8(deviceTypePtr) ?? "unknown";
                if (uuid != null)
                {
                    result.DeviceId = uuid;
                    result.DeviceName = TryDecodeName(nameB64);
                    result.Port = port;
                    result.BatteryLevel = battery < 0 ? Math.Abs(battery) : battery;
                    result.IsCharging = battery >= 0;
                    result.DeviceType = deviceType;
                    result.HasValue = true;
                }
            }, IntPtr.Zero);
        }
        return result.HasValue ? result : null;
    }

    private static string TryDecodeName(string? nameB64)
    {
        if (string.IsNullOrEmpty(nameB64)) return "unknown";
        try
        {
            var bytes = Convert.FromBase64String(nameB64);
            return Encoding.UTF8.GetString(bytes);
        }
        catch
        {
            return nameB64;
        }
    }

    /// <summary>
    /// 标记设备在线（由 Rust 心跳回调或手动调用）
    /// </summary>
    private void MarkDeviceAlive(PairedDevice device)
    {
        device.LastHeartbeat = DateTime.UtcNow;
    }
}

/// <summary>
/// 可复用的 UdpHeartbeatInfo，避免在回调中额外分配
/// </summary>
public class UdpHeartbeatInfo
{
    public string DeviceId { get; set; } = string.Empty;
    public string DeviceName { get; set; } = string.Empty;
    public int Port { get; set; }
    public int BatteryLevel { get; set; }
    public bool IsCharging { get; set; }
    public string DeviceType { get; set; } = string.Empty;
    public bool HasValue { get; set; }
}

/// <summary>
/// 包装 UdpHeartbeatInfo 的临时构建器（用于回调中填充）
/// </summary>
internal class UdpHeartbeatBuilder : UdpHeartbeatInfo { }


