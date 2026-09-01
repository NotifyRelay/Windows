using System.Globalization;
using System.Runtime.CompilerServices;
using Microsoft.UI;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using NotifyRelay.Data.Models;
using NotifyRelay.Helpers;
using NotifyRelay.Models.Render;

namespace NotifyRelay.Converters;

/// <summary>
/// The generic base implementation of a value converter.
/// </summary>
/// <typeparam name="TSource">The source type.</typeparam>
/// <typeparam name="TTarget">The target type.</typeparam>
internal abstract class ValueConverter<TSource, TTarget> : IValueConverter
{
    /// <summary>
    /// Converts a source value to the target type.
    /// </summary>
    /// <param name="value"></param>
    /// <returns></returns>
    public TTarget? Convert(TSource? value)
    {
        return Convert(value, null, null);
    }

    /// <summary>
    /// Converts a target value back to the source type.
    /// </summary>
    /// <param name="value"></param>
    /// <returns></returns>
    public TSource? ConvertBack(TTarget? value)
    {
        return ConvertBack(value, null, null);
    }

    /// <summary>
    /// Modifies the source data before passing it to the target for display in the UI.
    /// </summary>
    /// <param name="value"></param>
    /// <param name="targetType"></param>
    /// <param name="parameter"></param>
    /// <param name="language"></param>
    /// <returns></returns>
    public object? Convert(object? value, Type? targetType, object? parameter, string? language)
    {
        // CastExceptions will occur when invalid value, or target type provided.
        return Convert((TSource?)value, parameter, language);
    }

    /// <summary>
    /// Modifies the target data before passing it to the source object. This method is called only in TwoWay bindings.
    /// </summary>
    /// <param name="value"></param>
    /// <param name="targetType"></param>
    /// <param name="parameter"></param>
    /// <param name="language"></param>
    /// <returns></returns>
    public object? ConvertBack(object? value, Type? targetType, object? parameter, string? language)
    {
        // CastExceptions will occur when invalid value, or target type provided.
        return ConvertBack((TTarget?)value, parameter, language);
    }

    /// <summary>
    /// Converts a source value to the target type.
    /// </summary>
    /// <param name="value"></param>
    /// <param name="parameter"></param>
    /// <param name="language"></param>
    /// <returns></returns>
    protected virtual TTarget? Convert(TSource? value, object? parameter, string? language)
    {
        throw new NotSupportedException();
    }

    /// <summary>
    /// Converts a target value back to the source type.
    /// </summary>
    /// <param name="value"></param>
    /// <param name="parameter"></param>
    /// <param name="language"></param>
    /// <returns></returns>
    protected virtual TSource? ConvertBack(TTarget? value, object? parameter, string? language)
    {
        throw new NotSupportedException();
    }
}

/// <summary>
/// The base class for converting instances of type T to object and vice versa.
/// </summary>
internal abstract class ToObjectConverter<T> : ValueConverter<T?, object?>
{
    /// <summary>
    /// Converts a source value to the target type.
    /// </summary>
    /// <param name="value"></param>
    /// <param name="parameter"></param>
    /// <param name="language"></param>
    /// <returns></returns>
    protected override object? Convert(T? value, object? parameter, string? language)
    {
        return value;
    }

    /// <summary>
    /// Converts a target value back to the source type.
    /// </summary>
    /// <param name="value"></param>
    /// <param name="parameter"></param>
    /// <param name="language"></param>
    /// <returns></returns>
    protected override T? ConvertBack(object? value, object? parameter, string? language)
    {
        return (T?)value;
    }
}

internal sealed partial class EmptyObjectToVisibilityConverter : IValueConverter
{
    public bool Inverse { get; set; }

    public object Convert(object value, Type targetType, object parameter, string language)
    {
        if (Inverse)
            return value == null ? Visibility.Visible : Visibility.Collapsed;
        else
            return value == null ? Visibility.Collapsed : Visibility.Visible;
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        throw new NotImplementedException();
    }
}

internal sealed partial class BooleanToVisibilityConverter : ValueConverter<bool, Visibility>
{
    public bool Inverse { get; set; }

    protected override Visibility Convert(bool value, object? parameter, string? language)
    {
        return Inverse ? !value ? Visibility.Visible : Visibility.Collapsed : value ? Visibility.Visible : Visibility.Collapsed;
    }

    protected override bool ConvertBack(Visibility value, object? parameter, string? language)
    {
        throw new NotSupportedException();
    }
}

internal sealed partial class StringToImageSourceConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        if (value is string url && !string.IsNullOrEmpty(url))
        {
            try
            {
                // 直接尝试将URL转换为BitmapImage，不管它是什么格式
                // 对于base64 data URL和ms-appdata URL都适用
                var bitmapImage = new BitmapImage();

                if (url.StartsWith("data:image/"))
                {
                    // 处理base64 data URL
                    var base64Data = url.Substring(url.IndexOf(",") + 1);
                    var bytes = System.Convert.FromBase64String(base64Data);

                    // 使用MemoryStream加载图片数据
                    using (var stream = new MemoryStream(bytes))
                    {
                        stream.Seek(0, SeekOrigin.Begin);
                        bitmapImage.SetSource(stream.AsRandomAccessStream());
                    }

                    return bitmapImage;
                }
                else
                {
                    // 处理普通URL
                    bitmapImage.UriSource = new Uri(url);
                    return bitmapImage;
                }
            }
            catch (Exception ex)
            {
                // 记录错误，但不抛出异常
                System.Diagnostics.Debug.WriteLine($"StringToImageSourceConverter: {ex.Message}");
            }
        }
        return null!;
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        throw new NotImplementedException();
    }
}

internal sealed partial class CountToVisibilityConverter : IValueConverter
{
    public bool Inverse { get; set; }

    public object Convert(object value, Type targetType, object parameter, string language)
    {
        int count = value is int intCount ? intCount : 0;
        bool isEmpty = count == 0;

        if (Inverse)
            return isEmpty ? Visibility.Visible : Visibility.Collapsed;
        else
            return isEmpty ? Visibility.Collapsed : Visibility.Visible;
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        throw new NotImplementedException();
    }
}

internal sealed partial class DateTimeConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        if (value is string timestampStr && DateTime.TryParseExact(timestampStr, "yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime timestamp))
        {
            if (timestamp.Date == DateTime.Today)
            {
                // Return only the time if the date is the same as today
                return timestamp.ToString("t"); // Short time pattern
            }
            else
            {
                // Return the short date and time pattern otherwise
                return timestamp.ToString("g"); // Short date and time pattern
            }
        }

        return string.Empty; // Return an empty string if the timestamp is null or invalid
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        throw new NotImplementedException();
    }
}

internal sealed partial class BatteryStatusToIconConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        if (value is DeviceStatus deviceStatus)
        {
            // 调用共享工具（唯一真值）：主页与叠加层、罗技电池设置页共用同一段 Glyph 规则
            return BatteryIconUtility.GetGlyph(deviceStatus.BatteryStatus, deviceStatus.ChargingStatus);
        }

        return BatteryIconUtility.GetGlyph(100, false); // 默认满格非充电
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        throw new NotImplementedException();
    }
}

internal sealed partial class BatteryStatusToColorConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        if (value is DeviceStatus deviceStatus)
        {
            var (r, g, b) = BatteryIconUtility.GetColorBytes(deviceStatus.BatteryStatus, deviceStatus.ChargingStatus);
            return new SolidColorBrush(Windows.UI.Color.FromArgb(255, r, g, b));
        }

        var (dr, dg, db) = BatteryIconUtility.GetColorBytes(100, false);
        return new SolidColorBrush(Windows.UI.Color.FromArgb(255, dr, dg, db));
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        throw new NotImplementedException();
    }
}
internal sealed partial class RingerModeToIconConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        if (value is int ringerMode)
        {
            return ringerMode switch
            {
                2 => "\uE995",    // Normal (Speaker icon)
                1 => "\uE877",    // Vibrate icon
                0 => "\uE74F",    // Silent (Mute icon)
                _ => "\uE995"     // Default to speaker icon
            };
        }

        return "\uE995"; // Default icon
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        throw new NotImplementedException();
    }
}

internal sealed partial class AdbIconToTypeConverter : IValueConverter
{
    public static readonly AdbIconToTypeConverter Instance = new();

    public object Convert(object value, Type targetType, object parameter, string language)
    {
        if (value is string icon)
        {
            return icon switch
            {
                "\uE89E" => "USB",
                "\uE927" => "WiFi",
                _ => string.Empty
            };
        }
        return string.Empty;
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        throw new NotImplementedException();
    }
}

internal sealed partial class BoolToOpacityConverter : ValueConverter<bool, double>
{
    protected override double Convert(bool value, object? parameter, string? language)
    {
        return value ? 1.0 : 0.0;
    }

    protected override bool ConvertBack(double value, object? parameter, string? language)
    {
        throw new NotSupportedException();
    }
}

internal sealed partial class LampArrayKindToGlyphConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        if (value is Windows.Devices.Lights.LampArrayKind kind)
        {
            string kindName = kind.ToString();
            return kindName switch
            {
                "Keyboard" => "\uE94E",
                "Mouse" => "\uE94F",
                "Microphone" => "\uE724",
                "Headset" => "\uE72E",
                _ => "\uE7CB"
            };
        }
        return "\uE7CB";
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        throw new NotImplementedException();
    }
}

internal sealed partial class BoolToStatusConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        return value is bool b && b ? "可用" : "不可用";
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        throw new NotImplementedException();
    }
}

internal sealed partial class BoolToStatusColorConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        return value is bool b && b
            ? new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Green)
            : new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Gray);
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        throw new NotImplementedException();
    }
}

internal sealed partial class DoubleToPercentConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        if (value is double d)
        {
            return $"{Math.Round(d * 100)}%";
        }
        return "0%";
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        throw new NotImplementedException();
    }
}

internal sealed partial class ColorToStringConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        if (value is Windows.UI.Color color)
        {
            return $"#{color.A:X2}{color.R:X2}{color.G:X2}{color.B:X2}";
        }
        return "#00000000";
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        throw new NotImplementedException();
    }
}

// ================== 叠加层 LogiBattery 专用 Converter（统一调用共享 BatteryIconUtility，与主页同款） ==================

/// <summary>
/// LogiBatteryDeviceInfo → Segoe MDL2 Assets 图标 Glyph（与主页电池完全一致）。
/// 输入：LogiBatteryDeviceInfo 实例；输出：string Glyph 字串（单个 Unicode 字符）。
/// </summary>
internal sealed partial class LogiBatteryStatusToGlyphConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        if (value is not LogiBatteryDeviceInfo d)
            return BatteryIconUtility.GetGlyph(100, false);
        if (!d.HasBattery)
            return BatteryIconUtility.GetGlyph(100, false);
        return BatteryIconUtility.GetGlyph(d.BatteryPercent, d.IsCharging);
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language) => throw new NotImplementedException();
}

/// <summary>
/// LogiBatteryDeviceInfo → 颜色 SolidColorBrush（与主页电池完全一致）。
/// 也可用于 bool Online：online=true 绿色，offline=false 灰色。
/// </summary>
internal sealed partial class LogiBatteryStatusToBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        if (value is bool online)
        {
            // 参数模式：用于在线/离线小圆点
            var color = online ? Windows.UI.Color.FromArgb(255, 16, 185, 129) : Windows.UI.Color.FromArgb(255, 150, 150, 150);
            return new SolidColorBrush(color);
        }
        if (value is not LogiBatteryDeviceInfo d)
            return new SolidColorBrush(Windows.UI.Color.FromArgb(255, 128, 128, 128));
        if (!d.HasBattery || d.BatteryPercent < 0)
            return new SolidColorBrush(Windows.UI.Color.FromArgb(255, 128, 128, 128));
        var (r, g, b) = BatteryIconUtility.GetColorBytes(d.BatteryPercent, d.IsCharging);
        return new SolidColorBrush(Windows.UI.Color.FromArgb(255, r, g, b));
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language) => throw new NotImplementedException();
}

/// <summary>
/// LogiBatteryDeviceInfo.IsCharging → 人类可读文本。
/// 百分比后缀：输入 int 百分比→ "xx%" ，无电量为 "--" 。
/// </summary>
internal sealed partial class LogiBatteryTextConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        if (value is bool charging) return charging ? "充电中" : "使用电池";
        if (value is int percent) return percent >= 0 ? $"{percent}%" : "--";
        if (value is LogiBatteryDeviceInfo d) return d.IsCharging ? "充电中" : "使用电池";
        return string.Empty;
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language) => throw new NotImplementedException();
}

