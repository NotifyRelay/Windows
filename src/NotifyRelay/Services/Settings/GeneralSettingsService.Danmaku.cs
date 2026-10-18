namespace NotifyRelay.Services.Settings;

internal sealed partial class GeneralSettingsService
{
    // 弹幕叠加层设置
    public bool DanmakuNotificationEnabled
    {
        get => _settings.Get(nameof(DanmakuNotificationEnabled), true);
        set => _settings.Set(nameof(DanmakuNotificationEnabled), value);
    }

    public bool DanmakuMediaCardEnabled
    {
        get => _settings.Get(nameof(DanmakuMediaCardEnabled), true);
        set => _settings.Set(nameof(DanmakuMediaCardEnabled), value);
    }

    public bool DanmakuSuperIslandEnabled
    {
        get => _settings.Get(nameof(DanmakuSuperIslandEnabled), true);
        set => _settings.Set(nameof(DanmakuSuperIslandEnabled), value);
    }

    public bool GamebarRelayEnabled
    {
        get => _settings.Get(nameof(GamebarRelayEnabled), false);
        set => _settings.Set(nameof(GamebarRelayEnabled), value);
    }

    public int DanmakuFontSizePercent
    {
        get => _settings.Get(nameof(DanmakuFontSizePercent), 50);
        set => _settings.Set(nameof(DanmakuFontSizePercent), value);
    }

    public int DanmakuSpeed
    {
        get => _settings.Get(nameof(DanmakuSpeed), 3);
        set => _settings.Set(nameof(DanmakuSpeed), value);
    }

    public int DanmakuOpacityPercent
    {
        get => _settings.Get(nameof(DanmakuOpacityPercent), 100);
        set => _settings.Set(nameof(DanmakuOpacityPercent), value);
    }

    public int DanmakuDisplayAreaPercent
    {
        get => _settings.Get(nameof(DanmakuDisplayAreaPercent), 100);
        set => _settings.Set(nameof(DanmakuDisplayAreaPercent), value);
    }

    public int DanmakuDensity
    {
        get => _settings.Get(nameof(DanmakuDensity), 0);
        set => _settings.Set(nameof(DanmakuDensity), value);
    }

    public string DanmakuFontFamily
    {
        get => _settings.Get(nameof(DanmakuFontFamily), "Microsoft YaHei");
        set => _settings.Set(nameof(DanmakuFontFamily), value);
    }

    public bool DanmakuBold
    {
        get => _settings.Get(nameof(DanmakuBold), true);
        set => _settings.Set(nameof(DanmakuBold), value);
    }

    public string DanmakuColor
    {
        get => _settings.Get(nameof(DanmakuColor), "#FFFFFF");
        set => _settings.Set(nameof(DanmakuColor), value);
    }

    public bool DanmakuBorderEnabled
    {
        get => _settings.Get(nameof(DanmakuBorderEnabled), true);
        set => _settings.Set(nameof(DanmakuBorderEnabled), value);
    }

    public int DanmakuBorderThickness
    {
        get => _settings.Get(nameof(DanmakuBorderThickness), 2);
        set => _settings.Set(nameof(DanmakuBorderThickness), value);
    }

    public string DanmakuBorderColor
    {
        get => _settings.Get(nameof(DanmakuBorderColor), "#000000");
        set => _settings.Set(nameof(DanmakuBorderColor), value);
    }

    public bool DanmakuShadowEnabled
    {
        get => _settings.Get(nameof(DanmakuShadowEnabled), true);
        set => _settings.Set(nameof(DanmakuShadowEnabled), value);
    }

    public int DanmakuShadowDepth
    {
        get => _settings.Get(nameof(DanmakuShadowDepth), 2);
        set => _settings.Set(nameof(DanmakuShadowDepth), value);
    }

    public int DanmakuShadowOpacity
    {
        get => _settings.Get(nameof(DanmakuShadowOpacity), 100);
        set => _settings.Set(nameof(DanmakuShadowOpacity), value);
    }

    public string DanmakuShadowColor
    {
        get => _settings.Get(nameof(DanmakuShadowColor), "#000000");
        set => _settings.Set(nameof(DanmakuShadowColor), value);
    }

    public int DanmakuDisplayScreenMode
    {
        get => _settings.Get(nameof(DanmakuDisplayScreenMode), 0);
        set => _settings.Set(nameof(DanmakuDisplayScreenMode), value);
    }

    public int DanmakuPerformanceMode
    {
        get => _settings.Get(nameof(DanmakuPerformanceMode), 0);
        set => _settings.Set(nameof(DanmakuPerformanceMode), value);
    }
}
