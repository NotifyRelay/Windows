using NotifyRelay.Data.Configuration;

namespace NotifyRelay.Services.Settings;

internal sealed partial class GeneralSettingsService
{
    // 动态光效设置
    public bool EnableDynamicLighting
    {
        get => _configuration.Get(SettingsKey(nameof(EnableDynamicLighting)), false);
        set => _configuration.Set(SettingsKey(nameof(EnableDynamicLighting)), value);
    }

    public bool EnableAutoRGB
    {
        get => _configuration.Get(SettingsKey(nameof(EnableAutoRGB)), false);
        set => _configuration.Set(SettingsKey(nameof(EnableAutoRGB)), value);
    }

    public double DynamicLightingBrightness
    {
        get => _configuration.Get(SettingsKey(nameof(DynamicLightingBrightness)), 1.0);
        set => _configuration.Set(SettingsKey(nameof(DynamicLightingBrightness)), value);
    }

    public string? DynamicLightingColor
    {
        get => _configuration.Get<string?>(SettingsKey(nameof(DynamicLightingColor)), null);
        set => _configuration.Set(SettingsKey(nameof(DynamicLightingColor)), value);
    }

    public string? DynamicLightingEffect
    {
        get => _configuration.Get<string?>(SettingsKey(nameof(DynamicLightingEffect)), null);
        set => _configuration.Set(SettingsKey(nameof(DynamicLightingEffect)), value);
    }

    public int AutoRGBUpdateInterval
    {
        get => _configuration.Get(SettingsKey(nameof(AutoRGBUpdateInterval)), 5000);
        set => _configuration.Set(SettingsKey(nameof(AutoRGBUpdateInterval)), value);
    }
}
