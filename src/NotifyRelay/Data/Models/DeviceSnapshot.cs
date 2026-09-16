using System.Text.Json.Serialization;

namespace NotifyRelay.Data.Models;

/// <summary>
/// Rust core <c>nrc_get_device_list</c> 快照条目（平台端的只读投影）。
///
/// 字段与 FFI 返回一一对应，<b>平台端不再自行维护第二份真相</b>：
/// 在线/离线、配对状态、可见性过滤全部由 core 决定。
///
/// 仅 <see cref="Name"/> 与 <see cref="DeviceType"/> 允许使用「上帧非空值」兜底，
/// 因为 core 的 deviceType 不落库、name 在重启首帧可能为空。
/// </summary>
public sealed class DeviceSnapshot
{
    /// <summary>core 未识别设备类型时的占位值</summary>
    public const string UnknownDeviceType = "unknown";

    /// <summary>电量绝对值超过该值视为未知</summary>
    public const int BatteryUnknownThreshold = 100;

    /// <summary>电量未知哨兵（与 core BATTERY_UNKNOWN 一致）</summary>
    public const int BatteryUnknown = -BatteryUnknownThreshold - 1;

    [JsonPropertyName("uuid")]
    public string Uuid { get; init; } = string.Empty;

    /// <summary>设备名；core 重启首帧可能为空</summary>
    [JsonPropertyName("name")]
    public string Name { get; init; } = string.Empty;

    [JsonPropertyName("ip")]
    public string Ip { get; init; } = string.Empty;

    [JsonPropertyName("port")]
    public int Port { get; init; }

    /// <summary>带符号电量：正=充电，负=放电；|v|&gt;100 视为未知</summary>
    [JsonPropertyName("battery")]
    public int Battery { get; init; } = BatteryUnknown;

    /// <summary>设备类型，如 android / pc；core 不落库，重启首帧可能为空</summary>
    [JsonPropertyName("deviceType")]
    public string DeviceType { get; init; } = string.Empty;

    [JsonPropertyName("lastSeen")]
    public long LastSeen { get; init; }

    /// <summary>TCP 会话存在性（仅展示用，在线判定以 <see cref="Online"/> 为准）</summary>
    [JsonPropertyName("connected")]
    public bool Connected { get; init; }

    /// <summary>是否已配对（core：device_keys 命中或 is_accepted）</summary>
    [JsonPropertyName("paired")]
    public bool Paired { get; init; }

    /// <summary>是否在线（阈值由 core 内建：已认证 12s / 未认证 20s）</summary>
    [JsonPropertyName("online")]
    public bool Online { get; init; }

    /// <summary>电量是否未知</summary>
    public bool BatteryUnknownValue => Math.Abs(Battery) > BatteryUnknownThreshold;

    /// <summary>电量百分比；未知时为 -1</summary>
    public int BatteryPercent => BatteryUnknownValue ? -1 : Math.Abs(Battery);

    /// <summary>是否充电中；电量未知时为 false</summary>
    public bool IsCharging => !BatteryUnknownValue && Battery >= 0;

    /// <summary>设备类型是否有效</summary>
    public bool HasKnownDeviceType =>
        !string.IsNullOrWhiteSpace(DeviceType) && DeviceType != UnknownDeviceType;

    /// <summary>最后可见时间；无记录时取当前时间</summary>
    public DateTimeOffset LastSeenTime =>
        LastSeen > 0 ? DateTimeOffset.FromUnixTimeSeconds(LastSeen) : DateTimeOffset.UtcNow;

    /// <summary>端口；无有效值时使用回退端口</summary>
    public int PortOrDefault(int fallback) => Port > 0 ? Port : fallback;

    /// <summary>展示名；快照为空时依次使用回退名、uuid。</summary>
    public string DisplayName(string? fallback)
    {
        if (!string.IsNullOrWhiteSpace(Name)) return Name;
        if (!string.IsNullOrWhiteSpace(fallback)) return fallback;
        return Uuid;
    }
}
