namespace NotifyRelay.Services.Settings;

internal sealed partial class GeneralSettingsService
{
    public string? DeepSeekApiToken
    {
        get => _settings.Get<string?>(nameof(DeepSeekApiToken), null);
        set => _settings.Set(nameof(DeepSeekApiToken), value);
    }

    public bool EnableDeepSeekBalanceMonitor
    {
        get => _settings.Get(nameof(EnableDeepSeekBalanceMonitor), false);
        set => _settings.Set(nameof(EnableDeepSeekBalanceMonitor), value);
    }

    public int DeepSeekBalancePollingInterval
    {
        get => _settings.Get(nameof(DeepSeekBalancePollingInterval), 60000);
        set => _settings.Set(nameof(DeepSeekBalancePollingInterval), value);
    }

    public string? DeepSeekBalanceHistoryJson
    {
        get => _settings.Get<string?>(nameof(DeepSeekBalanceHistoryJson), null);
        set => _settings.Set(nameof(DeepSeekBalanceHistoryJson), value);
    }

    public bool DeepSeekBalanceHistoryCollapsed
    {
        get => _settings.Get(nameof(DeepSeekBalanceHistoryCollapsed), false);
        set => _settings.Set(nameof(DeepSeekBalanceHistoryCollapsed), value);
    }

    // ======== DeepSeek 余额叠加层（实现 IGeneralSettingsService 与 IOverlaySettings 共有契约） ========
    public string DeepSeekBalanceTargetScreen
    {
        get => _settings.Get(nameof(DeepSeekBalanceTargetScreen), "PRIMARY");
        set => _settings.Set(nameof(DeepSeekBalanceTargetScreen), value);
    }

    public int DeepSeekBalanceXPercent
    {
        get => _settings.Get(nameof(DeepSeekBalanceXPercent), 42);
        set => _settings.Set(nameof(DeepSeekBalanceXPercent), Math.Clamp(value, 0, 100));
    }

    public int DeepSeekBalanceYPercent
    {
        // 与心率(21,97)、罗技电池(53,98)、时间浮窗(50,100)错开，避免同类卡片默认重叠
        get => _settings.Get(nameof(DeepSeekBalanceYPercent), 100);
        set => _settings.Set(nameof(DeepSeekBalanceYPercent), Math.Clamp(value, 0, 100));
    }

    public float DeepSeekBalanceScale
    {
        get => _settings.Get(nameof(DeepSeekBalanceScale), 0.7f);
        set => _settings.Set(nameof(DeepSeekBalanceScale), Math.Clamp(value, 0.5f, 4f));
    }
}
