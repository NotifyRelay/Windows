using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("NotifyRelay")]

namespace NotifyRelay.Helpers;

/// <summary>
/// 电池图标/颜色共享工具（不依赖 WinUI）。
/// 唯一真值来源：主页 BatteryStatusToIconConverter / BatteryStatusToColorConverter
/// 和叠加层 Direct2D 渲染层均调用本类方法，保证显示一致，不重复逻辑。
/// </summary>
public static class BatteryIconUtility
{
    /// <summary>
    /// 根据电量百分比和充电状态返回 Segoe MDL2 Assets 字体的单个 Unicode Glyph。
    /// 与 Converters.BatteryStatusToIconConverter 使用相同分段规则。
    /// </summary>
    /// <param name="batteryPercent">电量 0-100（无数据时建议传 -1，走未知分支）</param>
    /// <param name="isCharging">是否处于充电中（含 Charging/ChargingSlow）；Full 按非充电显示满格图标即可，可传 false</param>
    public static string GetGlyph(int batteryPercent, bool isCharging)
    {
        if (isCharging)
        {
            return batteryPercent switch
            {
                >= 100 => "\uEA93",
                >= 90  => "\uE83E",
                >= 80  => "\uE862",
                >= 70  => "\uE861",
                >= 60  => "\uE860",
                >= 50  => "\uE85F",
                >= 40  => "\uE85E",
                >= 30  => "\uE85D",
                >= 20  => "\uE85C",
                >= 10  => "\uE85B",
                _      => "\uE85A"
            };
        }

        return batteryPercent switch
        {
            >= 100 => "\uE83F",
            >= 90  => "\uE859",
            >= 80  => "\uE858",
            >= 70  => "\uE857",
            >= 60  => "\uE856",
            >= 50  => "\uE855",
            >= 40  => "\uE854",
            >= 30  => "\uE853",
            >= 20  => "\uE852",
            >= 10  => "\uE851",
            _      => "\uE850"
        };
    }

    /// <summary>
    /// 根据电量百分比和充电状态返回 (R, G, B) 三原色字节。
    /// 与 Converters.BatteryStatusToColorConverter 使用相同颜色规则。
    /// </summary>
    public static (byte r, byte g, byte b) GetColorBytes(int batteryPercent, bool isCharging)
    {
        if (isCharging)
        {
            return (0, 128, 0); // Green
        }

        return batteryPercent switch
        {
            < 20 => (255, 0, 0),   // Red
            < 50 => (255, 255, 0), // Yellow
            _    => (0, 128, 0)    // Green
        };
    }
}
