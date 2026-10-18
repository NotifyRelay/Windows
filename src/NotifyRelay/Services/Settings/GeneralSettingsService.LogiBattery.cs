namespace NotifyRelay.Services.Settings;

internal sealed partial class GeneralSettingsService
{
    // ======== 罗技电池叠加层（实现 IGeneralSettingsService 与 IOverlaySettings 共有契约） ========
    public bool LogiBatteryEnabled
    {
        get => _settings.Get(nameof(LogiBatteryEnabled), false);
        set => _settings.Set(nameof(LogiBatteryEnabled), value);
    }

    public string LogiBatteryTargetScreen
    {
        get => _settings.Get(nameof(LogiBatteryTargetScreen), "PRIMARY");
        set => _settings.Set(nameof(LogiBatteryTargetScreen), value);
    }

    public int LogiBatteryXPercent
    {
        get => _settings.Get(nameof(LogiBatteryXPercent), 53);
        set => _settings.Set(nameof(LogiBatteryXPercent), Math.Clamp(value, 0, 100));
    }

    public int LogiBatteryYPercent
    {
        get => _settings.Get(nameof(LogiBatteryYPercent), 98);
        set => _settings.Set(nameof(LogiBatteryYPercent), Math.Clamp(value, 0, 100));
    }

    public float LogiBatteryScale
    {
        get => _settings.Get(nameof(LogiBatteryScale), 0.7f);
        set => _settings.Set(nameof(LogiBatteryScale), Math.Clamp(value, 0.5f, 4f));
    }

    public bool LogiBatteryHideWhenDisconnected
    {
        get => _settings.Get(nameof(LogiBatteryHideWhenDisconnected), true);
        set => _settings.Set(nameof(LogiBatteryHideWhenDisconnected), value);
    }

    public Dictionary<string, string> LogiBatteryDeviceNameOverrides
    {
        get => _settings.Get(nameof(LogiBatteryDeviceNameOverrides), new Dictionary<string, string>());
        set => _settings.Set(nameof(LogiBatteryDeviceNameOverrides), value);
    }
}
