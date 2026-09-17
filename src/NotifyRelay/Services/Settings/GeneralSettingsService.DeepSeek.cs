using NotifyRelay.Data.Configuration;

namespace NotifyRelay.Services.Settings;

internal sealed partial class GeneralSettingsService
{
    public string? DeepSeekApiToken
    {
        get => _configuration.Get<string?>(SettingsKey(nameof(DeepSeekApiToken)), null);
        set => _configuration.Set(SettingsKey(nameof(DeepSeekApiToken)), value);
    }

    public bool EnableDeepSeekBalanceMonitor
    {
        get => _configuration.Get(SettingsKey(nameof(EnableDeepSeekBalanceMonitor)), false);
        set => _configuration.Set(SettingsKey(nameof(EnableDeepSeekBalanceMonitor)), value);
    }

    public int DeepSeekBalancePollingInterval
    {
        get => _configuration.Get(SettingsKey(nameof(DeepSeekBalancePollingInterval)), 60000);
        set => _configuration.Set(SettingsKey(nameof(DeepSeekBalancePollingInterval)), value);
    }

    public string? DeepSeekBalanceHistoryJson
    {
        get => _configuration.Get<string?>(SettingsKey(nameof(DeepSeekBalanceHistoryJson)), null);
        set => _configuration.Set(SettingsKey(nameof(DeepSeekBalanceHistoryJson)), value);
    }

    public bool DeepSeekBalanceHistoryCollapsed
    {
        get => _configuration.Get(SettingsKey(nameof(DeepSeekBalanceHistoryCollapsed)), false);
        set => _configuration.Set(SettingsKey(nameof(DeepSeekBalanceHistoryCollapsed)), value);
    }

    // ======== DeepSeek 余额叠加层（实现 IGeneralSettingsService 与 IOverlaySettings 共有契约） ========
    public string DeepSeekBalanceTargetScreen
    {
        get => _configuration.Get(SettingsKey(nameof(DeepSeekBalanceTargetScreen)), "PRIMARY")!;
        set => _configuration.Set(SettingsKey(nameof(DeepSeekBalanceTargetScreen)), value);
    }

    public int DeepSeekBalanceXPercent
    {
        get => _configuration.Get(SettingsKey(nameof(DeepSeekBalanceXPercent)), 42);
        set => _configuration.Set(SettingsKey(nameof(DeepSeekBalanceXPercent)), Math.Clamp(value, 0, 100));
    }

    public int DeepSeekBalanceYPercent
    {
        // 与心率(21,97)、罗技电池(53,98)、时间浮窗(50,100)错开，避免同类卡片默认重叠
        get => _configuration.Get(SettingsKey(nameof(DeepSeekBalanceYPercent)), 100);
        set => _configuration.Set(SettingsKey(nameof(DeepSeekBalanceYPercent)), Math.Clamp(value, 0, 100));
    }

    public float DeepSeekBalanceScale
    {
        get => _configuration.Get(SettingsKey(nameof(DeepSeekBalanceScale)), 0.7f);
        set => _configuration.Set(SettingsKey(nameof(DeepSeekBalanceScale)), Math.Clamp(value, 0.5f, 4f));
    }
}
