using Windows.UI;

namespace NotifyRelay.Helpers;

/// <summary>
/// #RRGGBB 颜色字符串与 <see cref="Color"/> 之间的转换辅助。
/// 供两个设置页的 ColorPicker 与弹幕 / 覆盖层 ViewModel 共用，替代各自的重复实现。
/// </summary>
public static class ColorHex
{
    /// <summary>解析 #RRGGBB 中指定通道（offset 为 0/2/4），失败返回 fallback。</summary>
    public static byte TryParseChannel(string? hex, byte fallback, int offset)
    {
        if (string.IsNullOrEmpty(hex) || !hex.StartsWith('#')) return fallback;
        var s = hex.TrimStart('#');
        if (s.Length != 6) return fallback;
        return byte.TryParse(s.AsSpan(offset, 2), System.Globalization.NumberStyles.HexNumber, null, out byte v)
            ? v
            : fallback;
    }

    /// <summary>解析 #RRGGBB（可省 #）为不透明 <see cref="Color"/>；失败返回 null。</summary>
    public static Color? TryParse(string? hex)
    {
        if (string.IsNullOrEmpty(hex)) return null;
        var s = hex.TrimStart('#');
        if (s.Length != 6) return null;
        if (!byte.TryParse(s.AsSpan(0, 2), System.Globalization.NumberStyles.HexNumber, null, out byte r)
            || !byte.TryParse(s.AsSpan(2, 2), System.Globalization.NumberStyles.HexNumber, null, out byte g)
            || !byte.TryParse(s.AsSpan(4, 2), System.Globalization.NumberStyles.HexNumber, null, out byte b))
        {
            return null;
        }
        return Color.FromArgb(255, r, g, b);
    }

    /// <summary>解析为 <see cref="Color"/>，失败时返回 fallback（默认白色）。</summary>
    public static Color ToWindowsColor(string? hex, Color? fallback = null)
        => TryParse(hex) ?? fallback ?? Color.FromArgb(255, 255, 255, 255);

    /// <summary>格式化为 #RRGGBB。</summary>
    public static string Format(Color color) => $"#{color.R:X2}{color.G:X2}{color.B:X2}";
}
