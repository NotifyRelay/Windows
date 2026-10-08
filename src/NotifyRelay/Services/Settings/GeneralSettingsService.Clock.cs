namespace NotifyRelay.Services.Settings;

internal sealed partial class GeneralSettingsService
{
    // ======== 时间浮窗叠加层（实现 IGeneralSettingsService 与 IOverlaySettings 共有契约） ========
    public bool ClockOverlayEnabled
    {
        get => _settings.Get(nameof(ClockOverlayEnabled), false);
        set => _settings.Set(nameof(ClockOverlayEnabled), value);
    }

    public string ClockTargetScreen
    {
        get => _settings.Get(nameof(ClockTargetScreen), "PRIMARY");
        set => _settings.Set(nameof(ClockTargetScreen), value);
    }

    public int ClockXPercent
    {
        get => _settings.Get(nameof(ClockXPercent), 50);
        set => _settings.Set(nameof(ClockXPercent), Math.Clamp(value, 0, 100));
    }

    public int ClockYPercent
    {
        get => _settings.Get(nameof(ClockYPercent), 100);
        set => _settings.Set(nameof(ClockYPercent), Math.Clamp(value, 0, 100));
    }

    public string ClockColor
    {
        get => _settings.Get(nameof(ClockColor), "#FFFFFF");
        set => _settings.Set(nameof(ClockColor), value);
    }

    public float ClockTextOutlineWidth
    {
        get => _settings.Get(nameof(ClockTextOutlineWidth), 3f);
        set => _settings.Set(nameof(ClockTextOutlineWidth), Math.Clamp(value, 0.1f, 3f));
    }

    public float ClockScale
    {
        get => _settings.Get(nameof(ClockScale), 0.5f);
        set => _settings.Set(nameof(ClockScale), Math.Clamp(value, 0.5f, 2f));
    }

    public bool ClockShowSeconds
    {
        get => _settings.Get(nameof(ClockShowSeconds), true);
        set => _settings.Set(nameof(ClockShowSeconds), value);
    }

    public bool ClockUse24Hour
    {
        get => _settings.Get(nameof(ClockUse24Hour), true);
        set => _settings.Set(nameof(ClockUse24Hour), value);
    }
}
