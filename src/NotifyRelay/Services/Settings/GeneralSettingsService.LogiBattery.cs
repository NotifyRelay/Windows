using NotifyRelay.Data.Configuration;

namespace NotifyRelay.Services.Settings;

internal sealed partial class GeneralSettingsService
{
    // ======== 罗技电池叠加层（实现 IGeneralSettingsService 与 IOverlaySettings 共有契约） ========
    public bool LogiBatteryEnabled
    {
        get => _configuration.Get(SettingsKey(nameof(LogiBatteryEnabled)), false);
        set => _configuration.Set(SettingsKey(nameof(LogiBatteryEnabled)), value);
    }

    public string LogiBatteryTargetScreen
    {
        get => _configuration.Get(SettingsKey(nameof(LogiBatteryTargetScreen)), "PRIMARY")!;
        set => _configuration.Set(SettingsKey(nameof(LogiBatteryTargetScreen)), value);
    }

    public int LogiBatteryXPercent
    {
        get => _configuration.Get(SettingsKey(nameof(LogiBatteryXPercent)), 20);
        set => _configuration.Set(SettingsKey(nameof(LogiBatteryXPercent)), Math.Clamp(value, 0, 100));
    }

    public int LogiBatteryYPercent
    {
        get => _configuration.Get(SettingsKey(nameof(LogiBatteryYPercent)), 70);
        set => _configuration.Set(SettingsKey(nameof(LogiBatteryYPercent)), Math.Clamp(value, 0, 100));
    }

    public float LogiBatteryScale
    {
        get => _configuration.Get(SettingsKey(nameof(LogiBatteryScale)), 1f);
        set => _configuration.Set(SettingsKey(nameof(LogiBatteryScale)), Math.Clamp(value, 0.5f, 4f));
    }

    public bool LogiBatteryHideWhenDisconnected
    {
        get => _configuration.Get(SettingsKey(nameof(LogiBatteryHideWhenDisconnected)), true);
        set => _configuration.Set(SettingsKey(nameof(LogiBatteryHideWhenDisconnected)), value);
    }

    public Dictionary<string, string> LogiBatteryDeviceNameOverrides
    {
        get => _configuration.Get(SettingsKey(nameof(LogiBatteryDeviceNameOverrides)), new Dictionary<string, string>())!;
        set => _configuration.Set(SettingsKey(nameof(LogiBatteryDeviceNameOverrides)), value);
    }
}
