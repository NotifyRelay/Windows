namespace NotifyRelay.Services.Settings;

internal sealed partial class GeneralSettingsService
{
    // 动态光效设置
    public bool EnableDynamicLighting
    {
        get => _settings.Get(nameof(EnableDynamicLighting), false);
        set => _settings.Set(nameof(EnableDynamicLighting), value);
    }

    public bool EnableAutoRGB
    {
        get => _settings.Get(nameof(EnableAutoRGB), false);
        set => _settings.Set(nameof(EnableAutoRGB), value);
    }

    public double DynamicLightingBrightness
    {
        get => _settings.Get(nameof(DynamicLightingBrightness), 1.0);
        set => _settings.Set(nameof(DynamicLightingBrightness), value);
    }

    public string? DynamicLightingColor
    {
        get => _settings.Get<string?>(nameof(DynamicLightingColor), null);
        set => _settings.Set(nameof(DynamicLightingColor), value);
    }

    public string? DynamicLightingEffect
    {
        get => _settings.Get<string?>(nameof(DynamicLightingEffect), null);
        set => _settings.Set(nameof(DynamicLightingEffect), value);
    }

    public int AutoRGBUpdateInterval
    {
        get => _settings.Get(nameof(AutoRGBUpdateInterval), 5000);
        set => _settings.Set(nameof(AutoRGBUpdateInterval), value);
    }
}
