using NotifyRelay.Helpers;
using Vortice.Mathematics;

namespace NotifyRelay.Models.Render;

/// <summary>
/// 罗技电池设备渲染信息（Overlay 内部消费模型）。
/// 字段由主项目 LogiBatteryProvider 从 Rust FFI 的 LbDeviceInfo 转换而来。
/// </summary>
public sealed class LogiBatteryDeviceInfo
{
    /// <summary>USB Vendor ID（通常 0x046D = Logitech）。</summary>
    public ushort VendorId { get; init; }

    /// <summary>USB Product ID。</summary>
    public ushort ProductId { get; init; }

    /// <summary>设备稳定标识（用于刷新前后对比同一台设备）。</summary>
    public required string DeviceId { get; init; }

    /// <summary>人类可读设备名（NUL 结尾 UTF-8 转换而来；允许设置页用户手动覆盖赋值）。</summary>
    public required string DeviceName { get; set; }

    /// <summary>接收器配对槽位（0=直连；1..=6=接收器）。</summary>
    public byte Slot { get; init; }

    /// <summary>是否在线。</summary>
    public bool Online { get; init; }

    /// <summary>是否有电量数据。</summary>
    public bool HasBattery { get; init; }

    /// <summary>
    /// 电量百分比（0-100）。
    /// 约定：无电量/离线为 -1，供 HideWhenDisconnected 判断使用。
    /// </summary>
    public int BatteryPercent { get; init; }

    /// <summary>
    /// 对应 FFI LbBatteryStatus：0=Discharging 1=Charging 2=ChargingSlow 3=Full 4=Error 5=Unknown。
    /// </summary>
    public byte StatusRaw { get; init; }

    /// <summary>计算属性：是否充电（Charging/ChargingSlow → true；其余 false）。</summary>
    public bool IsCharging => StatusRaw is (byte)1 or (byte)2;

    /// <summary>Segoe MDL2 Assets 字体图标：调用共享工具（主页同款）。</summary>
    public string BatteryGlyph => HasBattery
        ? BatteryIconUtility.GetGlyph(BatteryPercent, IsCharging)
        : BatteryIconUtility.GetGlyph(100, false);

    /// <summary>图标颜色：调用共享工具（主页同款）。</summary>
    public Color4 BatteryColor
    {
        get
        {
            if (!HasBattery || BatteryPercent < 0)
            {
                return new Color4(0.5f, 0.5f, 0.5f, 1f); // 灰色表示未知
            }
            var (r, g, b) = BatteryIconUtility.GetColorBytes(BatteryPercent, IsCharging);
            return new Color4(r / 255f, g / 255f, b / 255f, 1f);
        }
    }
}
