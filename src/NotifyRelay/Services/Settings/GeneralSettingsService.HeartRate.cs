using NotifyRelay.Data.Configuration;

namespace NotifyRelay.Services.Settings;

internal sealed partial class GeneralSettingsService
{
    // 心率覆盖层设置
    public bool HeartRateOverlayEnabled
    {
        get => _configuration.Get(SettingsKey(nameof(HeartRateOverlayEnabled)), false);
        set => _configuration.Set(SettingsKey(nameof(HeartRateOverlayEnabled)), value);
    }

    public int HeartRateStyle
    {
        get => _configuration.Get(SettingsKey(nameof(HeartRateStyle)), 1);
        set => _configuration.Set(SettingsKey(nameof(HeartRateStyle)), value);
    }

    public string HeartRateTargetScreen
    {
        get => _configuration.Get(SettingsKey(nameof(HeartRateTargetScreen)), "PRIMARY")!;
        set => _configuration.Set(SettingsKey(nameof(HeartRateTargetScreen)), value);
    }

    public int HeartRateXPercent
    {
        get => _configuration.Get(SettingsKey(nameof(HeartRateXPercent)), 90);
        set => _configuration.Set(SettingsKey(nameof(HeartRateXPercent)), value);
    }

    public int HeartRateYPercent
    {
        get => _configuration.Get(SettingsKey(nameof(HeartRateYPercent)), 85);
        set => _configuration.Set(SettingsKey(nameof(HeartRateYPercent)), value);
    }

    public string HeartRateColor
    {
        get => _configuration.Get(SettingsKey(nameof(HeartRateColor)), "#FFFFFF")!;
        set => _configuration.Set(SettingsKey(nameof(HeartRateColor)), value);
    }

    public float HeartRateTextOutlineWidth
    {
        get => _configuration.Get(SettingsKey(nameof(HeartRateTextOutlineWidth)), 2f);
        set => _configuration.Set(SettingsKey(nameof(HeartRateTextOutlineWidth)), Math.Clamp(value, 0.1f, 3f));
    }

    public bool HeartRateAlertEnabled
    {
        get => _configuration.Get(SettingsKey(nameof(HeartRateAlertEnabled)), false);
        set => _configuration.Set(SettingsKey(nameof(HeartRateAlertEnabled)), value);
    }

    public int HeartRateLowAlert
    {
        get => _configuration.Get(SettingsKey(nameof(HeartRateLowAlert)), 50);
        set => _configuration.Set(SettingsKey(nameof(HeartRateLowAlert)), value);
    }

    public int HeartRateHighAlert
    {
        get => _configuration.Get(SettingsKey(nameof(HeartRateHighAlert)), 120);
        set => _configuration.Set(SettingsKey(nameof(HeartRateHighAlert)), value);
    }

    public int HeartRateSpikeDelta
    {
        get => _configuration.Get(SettingsKey(nameof(HeartRateSpikeDelta)), 20);
        set => _configuration.Set(SettingsKey(nameof(HeartRateSpikeDelta)), value);
    }

    public float HeartRateScale
    {
        get => _configuration.Get(SettingsKey(nameof(HeartRateScale)), 1f);
        set => _configuration.Set(SettingsKey(nameof(HeartRateScale)), Math.Clamp(value, 0.5f, 2f));
    }

    public bool HeartRateHideWhenDisconnected
    {
        get => _configuration.Get(SettingsKey(nameof(HeartRateHideWhenDisconnected)), true);
        set => _configuration.Set(SettingsKey(nameof(HeartRateHideWhenDisconnected)), value);
    }

    public bool HeartRateAutoConnectEnabled
    {
        get => _configuration.Get(SettingsKey(nameof(HeartRateAutoConnectEnabled)), false);
        set => _configuration.Set(SettingsKey(nameof(HeartRateAutoConnectEnabled)), value);
    }

    public string HeartRateLastDeviceAddress
    {
        get => _configuration.Get(SettingsKey(nameof(HeartRateLastDeviceAddress)), string.Empty)!;
        set => _configuration.Set(SettingsKey(nameof(HeartRateLastDeviceAddress)), value ?? string.Empty);
    }
}
