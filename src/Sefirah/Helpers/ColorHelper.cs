using Microsoft.UI;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace NotifyRelay.Helpers;

public static class ColorHelper
{
    public static SolidColorBrush CreateBrush(string colorHex)
    {
        if (string.IsNullOrEmpty(colorHex))
        {
            return new SolidColorBrush(Colors.White);
        }

        string hex = colorHex.TrimStart('#');
        byte r, g, b, a = 255;

        if (hex.Length == 6)
        {
            r = byte.Parse(hex.Substring(0, 2), System.Globalization.NumberStyles.HexNumber);
            g = byte.Parse(hex.Substring(2, 2), System.Globalization.NumberStyles.HexNumber);
            b = byte.Parse(hex.Substring(4, 2), System.Globalization.NumberStyles.HexNumber);
        }
        else if (hex.Length == 8)
        {
            a = byte.Parse(hex.Substring(0, 2), System.Globalization.NumberStyles.HexNumber);
            r = byte.Parse(hex.Substring(2, 2), System.Globalization.NumberStyles.HexNumber);
            g = byte.Parse(hex.Substring(4, 2), System.Globalization.NumberStyles.HexNumber);
            b = byte.Parse(hex.Substring(6, 2), System.Globalization.NumberStyles.HexNumber);
        }
        else
        {
            return new SolidColorBrush(Colors.White);
        }

        return new SolidColorBrush(new Color { A = a, R = r, G = g, B = b });
    }
}