namespace NotifyRelay.Models.Render;

public class DanmakuStyleSettings
{
    public double FontSizePercent { get; set; } = 100;
    public double Speed { get; set; } = 6;
    public double OpacityPercent { get; set; } = 100;
    public double DisplayAreaPercent { get; set; } = 100;
    public int Density { get; set; } = 0;
    public string FontFamilyName { get; set; } = "Microsoft YaHei";
    public bool Bold { get; set; } = true;
    public byte ColorR { get; set; } = 255;
    public byte ColorG { get; set; } = 255;
    public byte ColorB { get; set; } = 255;

    public bool BorderEnabled { get; set; }
    public double BorderThickness { get; set; } = 2;
    public byte BorderColorR { get; set; }
    public byte BorderColorG { get; set; }
    public byte BorderColorB { get; set; }

    public bool ShadowEnabled { get; set; } = true;
    public double ShadowBlur { get; set; }
    public double ShadowDepth { get; set; } = 2;
    public double ShadowOpacity { get; set; } = 100;
    public byte ShadowColorR { get; set; }
    public byte ShadowColorG { get; set; }
    public byte ShadowColorB { get; set; }

    public double FontSize => 36 * FontSizePercent / 100.0;
    public double PixelsPerSecond => Math.Max(1, Speed) * 60.0;
    public float Opacity => (float)(OpacityPercent / 100.0);
    public float ShadowOpacityFloat => (float)(ShadowOpacity / 100.0);
}
