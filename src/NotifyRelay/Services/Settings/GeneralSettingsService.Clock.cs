using NotifyRelay.Data.Configuration;

namespace NotifyRelay.Services.Settings;

internal sealed partial class GeneralSettingsService
{
    // ======== 时间浮窗叠加层（实现 IGeneralSettingsService 与 IOverlaySettings 共有契约） ========
    public bool ClockOverlayEnabled
    {
        get => _configuration.Get(SettingsKey(nameof(ClockOverlayEnabled)), false);
        set => _configuration.Set(SettingsKey(nameof(ClockOverlayEnabled)), value);
    }

    public string ClockTargetScreen
    {
        get => _configuration.Get(SettingsKey(nameof(ClockTargetScreen)), "PRIMARY")!;
        set => _configuration.Set(SettingsKey(nameof(ClockTargetScreen)), value);
    }

    public int ClockXPercent
    {
        get => _configuration.Get(SettingsKey(nameof(ClockXPercent)), 50);
        set => _configuration.Set(SettingsKey(nameof(ClockXPercent)), Math.Clamp(value, 0, 100));
    }

    public int ClockYPercent
    {
        get => _configuration.Get(SettingsKey(nameof(ClockYPercent)), 100);
        set => _configuration.Set(SettingsKey(nameof(ClockYPercent)), Math.Clamp(value, 0, 100));
    }

    public string ClockColor
    {
        get => _configuration.Get(SettingsKey(nameof(ClockColor)), "#FFFFFF")!;
        set => _configuration.Set(SettingsKey(nameof(ClockColor)), value);
    }

    public float ClockTextOutlineWidth
    {
        get => _configuration.Get(SettingsKey(nameof(ClockTextOutlineWidth)), 3f);
        set => _configuration.Set(SettingsKey(nameof(ClockTextOutlineWidth)), Math.Clamp(value, 0.1f, 3f));
    }

    public float ClockScale
    {
        get => _configuration.Get(SettingsKey(nameof(ClockScale)), 0.5f);
        set => _configuration.Set(SettingsKey(nameof(ClockScale)), Math.Clamp(value, 0.5f, 2f));
    }

    public bool ClockShowSeconds
    {
        get => _configuration.Get(SettingsKey(nameof(ClockShowSeconds)), true);
        set => _configuration.Set(SettingsKey(nameof(ClockShowSeconds)), value);
    }

    public bool ClockUse24Hour
    {
        get => _configuration.Get(SettingsKey(nameof(ClockUse24Hour)), true);
        set => _configuration.Set(SettingsKey(nameof(ClockUse24Hour)), value);
    }
}
