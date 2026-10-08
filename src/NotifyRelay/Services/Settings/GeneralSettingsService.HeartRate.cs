namespace NotifyRelay.Services.Settings;

internal sealed partial class GeneralSettingsService
{
    // 心率覆盖层设置
    public bool HeartRateOverlayEnabled
    {
        get => _settings.Get(nameof(HeartRateOverlayEnabled), false);
        set => _settings.Set(nameof(HeartRateOverlayEnabled), value);
    }

    public int HeartRateStyle
    {
        get => _settings.Get(nameof(HeartRateStyle), 4);
        set => _settings.Set(nameof(HeartRateStyle), value);
    }

    public string HeartRateTargetScreen
    {
        get => _settings.Get(nameof(HeartRateTargetScreen), "PRIMARY");
        set => _settings.Set(nameof(HeartRateTargetScreen), value);
    }

    public int HeartRateXPercent
    {
        get => _settings.Get(nameof(HeartRateXPercent), 21);
        set => _settings.Set(nameof(HeartRateXPercent), value);
    }

    public int HeartRateYPercent
    {
        get => _settings.Get(nameof(HeartRateYPercent), 97);
        set => _settings.Set(nameof(HeartRateYPercent), value);
    }

    public string HeartRateColor
    {
        get => _settings.Get(nameof(HeartRateColor), "#FFFFFF");
        set => _settings.Set(nameof(HeartRateColor), value);
    }

    public float HeartRateTextOutlineWidth
    {
        get => _settings.Get(nameof(HeartRateTextOutlineWidth), 2f);
        set => _settings.Set(nameof(HeartRateTextOutlineWidth), Math.Clamp(value, 0.1f, 3f));
    }

    public bool HeartRateAlertEnabled
    {
        get => _settings.Get(nameof(HeartRateAlertEnabled), false);
        set => _settings.Set(nameof(HeartRateAlertEnabled), value);
    }

    public int HeartRateLowAlert
    {
        get => _settings.Get(nameof(HeartRateLowAlert), 50);
        set => _settings.Set(nameof(HeartRateLowAlert), value);
    }

    public int HeartRateHighAlert
    {
        get => _settings.Get(nameof(HeartRateHighAlert), 120);
        set => _settings.Set(nameof(HeartRateHighAlert), value);
    }

    public int HeartRateSpikeDelta
    {
        get => _settings.Get(nameof(HeartRateSpikeDelta), 20);
        set => _settings.Set(nameof(HeartRateSpikeDelta), value);
    }

    public float HeartRateScale
    {
        get => _settings.Get(nameof(HeartRateScale), 0.6f);
        set => _settings.Set(nameof(HeartRateScale), Math.Clamp(value, 0.5f, 2f));
    }

    public bool HeartRateHideWhenDisconnected
    {
        get => _settings.Get(nameof(HeartRateHideWhenDisconnected), true);
        set => _settings.Set(nameof(HeartRateHideWhenDisconnected), value);
    }

    public bool HeartRateAutoConnectEnabled
    {
        get => _settings.Get(nameof(HeartRateAutoConnectEnabled), false);
        set => _settings.Set(nameof(HeartRateAutoConnectEnabled), value);
    }

    public string HeartRateLastDeviceAddress
    {
        get => _settings.Get(nameof(HeartRateLastDeviceAddress), string.Empty);
        set => _settings.Set(nameof(HeartRateLastDeviceAddress), value ?? string.Empty);
    }
}
