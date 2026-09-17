using NotifyRelay.Data.Configuration;

namespace NotifyRelay.Services.Settings;

internal sealed partial class GeneralSettingsService
{
    // 弹幕叠加层设置
    public bool DanmakuNotificationEnabled
    {
        get => _configuration.Get(SettingsKey(nameof(DanmakuNotificationEnabled)), true);
        set => _configuration.Set(SettingsKey(nameof(DanmakuNotificationEnabled)), value);
    }

    public bool DanmakuMediaCardEnabled
    {
        get => _configuration.Get(SettingsKey(nameof(DanmakuMediaCardEnabled)), true);
        set => _configuration.Set(SettingsKey(nameof(DanmakuMediaCardEnabled)), value);
    }

    public bool DanmakuSuperIslandEnabled
    {
        get => _configuration.Get(SettingsKey(nameof(DanmakuSuperIslandEnabled)), true);
        set => _configuration.Set(SettingsKey(nameof(DanmakuSuperIslandEnabled)), value);
    }

    public bool GamebarRelayEnabled
    {
        get => _configuration.Get(SettingsKey(nameof(GamebarRelayEnabled)), false);
        set => _configuration.Set(SettingsKey(nameof(GamebarRelayEnabled)), value);
    }

    public int DanmakuFontSizePercent
    {
        get => _configuration.Get(SettingsKey(nameof(DanmakuFontSizePercent)), 50);
        set => _configuration.Set(SettingsKey(nameof(DanmakuFontSizePercent)), value);
    }

    public int DanmakuSpeed
    {
        get => _configuration.Get(SettingsKey(nameof(DanmakuSpeed)), 3);
        set => _configuration.Set(SettingsKey(nameof(DanmakuSpeed)), value);
    }

    public int DanmakuOpacityPercent
    {
        get => _configuration.Get(SettingsKey(nameof(DanmakuOpacityPercent)), 100);
        set => _configuration.Set(SettingsKey(nameof(DanmakuOpacityPercent)), value);
    }

    public int DanmakuDisplayAreaPercent
    {
        get => _configuration.Get(SettingsKey(nameof(DanmakuDisplayAreaPercent)), 100);
        set => _configuration.Set(SettingsKey(nameof(DanmakuDisplayAreaPercent)), value);
    }

    public int DanmakuDensity
    {
        get => _configuration.Get(SettingsKey(nameof(DanmakuDensity)), 0);
        set => _configuration.Set(SettingsKey(nameof(DanmakuDensity)), value);
    }

    public string DanmakuFontFamily
    {
        get => _configuration.Get(SettingsKey(nameof(DanmakuFontFamily)), "Microsoft YaHei")!;
        set => _configuration.Set(SettingsKey(nameof(DanmakuFontFamily)), value);
    }

    public bool DanmakuBold
    {
        get => _configuration.Get(SettingsKey(nameof(DanmakuBold)), true);
        set => _configuration.Set(SettingsKey(nameof(DanmakuBold)), value);
    }

    public string DanmakuColor
    {
        get => _configuration.Get(SettingsKey(nameof(DanmakuColor)), "#FFFFFF")!;
        set => _configuration.Set(SettingsKey(nameof(DanmakuColor)), value);
    }

    public bool DanmakuBorderEnabled
    {
        get => _configuration.Get(SettingsKey(nameof(DanmakuBorderEnabled)), true);
        set => _configuration.Set(SettingsKey(nameof(DanmakuBorderEnabled)), value);
    }

    public int DanmakuBorderThickness
    {
        get => _configuration.Get(SettingsKey(nameof(DanmakuBorderThickness)), 2);
        set => _configuration.Set(SettingsKey(nameof(DanmakuBorderThickness)), value);
    }

    public string DanmakuBorderColor
    {
        get => _configuration.Get(SettingsKey(nameof(DanmakuBorderColor)), "#000000")!;
        set => _configuration.Set(SettingsKey(nameof(DanmakuBorderColor)), value);
    }

    public bool DanmakuShadowEnabled
    {
        get => _configuration.Get(SettingsKey(nameof(DanmakuShadowEnabled)), true);
        set => _configuration.Set(SettingsKey(nameof(DanmakuShadowEnabled)), value);
    }

    public int DanmakuShadowDepth
    {
        get => _configuration.Get(SettingsKey(nameof(DanmakuShadowDepth)), 2);
        set => _configuration.Set(SettingsKey(nameof(DanmakuShadowDepth)), value);
    }

    public int DanmakuShadowOpacity
    {
        get => _configuration.Get(SettingsKey(nameof(DanmakuShadowOpacity)), 100);
        set => _configuration.Set(SettingsKey(nameof(DanmakuShadowOpacity)), value);
    }

    public string DanmakuShadowColor
    {
        get => _configuration.Get(SettingsKey(nameof(DanmakuShadowColor)), "#000000")!;
        set => _configuration.Set(SettingsKey(nameof(DanmakuShadowColor)), value);
    }

    public int DanmakuDisplayScreenMode
    {
        get => _configuration.Get(SettingsKey(nameof(DanmakuDisplayScreenMode)), 0);
        set => _configuration.Set(SettingsKey(nameof(DanmakuDisplayScreenMode)), value);
    }

    public int DanmakuPerformanceMode
    {
        get => _configuration.Get(SettingsKey(nameof(DanmakuPerformanceMode)), 0);
        set => _configuration.Set(SettingsKey(nameof(DanmakuPerformanceMode)), value);
    }
}
