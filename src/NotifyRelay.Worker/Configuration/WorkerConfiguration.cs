namespace NotifyRelay.Worker.Configuration;

public class WorkerConfiguration
{
    public string? DeepSeekApiToken { get; set; }
    public int DeepSeekBalancePollingInterval { get; set; } = 60000;
    public string? DeepSeekBalanceHistoryJson { get; set; }

    public string? ControlMyMonitorPath { get; set; }
    public List<string> SelectedMonitors { get; set; } = [];
    public bool EnableMonitorBrightnessSync { get; set; }

    public double DynamicLightingBrightness { get; set; } = 1.0;
    public string? DynamicLightingColor { get; set; }
    public string? DynamicLightingEffect { get; set; }
    public bool EnableAutoRGB { get; set; }
    public int AutoRGBUpdateInterval { get; set; } = 5000;

    public void ApplyConfig(Dictionary<string, object?> config)
    {
        if (config.TryGetValue("deepSeekApiToken", out var token))
            DeepSeekApiToken = token as string;
        if (config.TryGetValue("deepSeekBalancePollingInterval", out var interval))
            DeepSeekBalancePollingInterval = Convert.ToInt32(interval);
        if (config.TryGetValue("deepSeekBalanceHistoryJson", out var history))
            DeepSeekBalanceHistoryJson = history as string;
        if (config.TryGetValue("controlMyMonitorPath", out var path))
            ControlMyMonitorPath = path as string;
        if (config.TryGetValue("selectedMonitors", out var monitors) && monitors is List<string> list)
            SelectedMonitors = list;
        if (config.TryGetValue("enableMonitorBrightnessSync", out var sync))
            EnableMonitorBrightnessSync = Convert.ToBoolean(sync);
        if (config.TryGetValue("dynamicLightingBrightness", out var brightness))
            DynamicLightingBrightness = Convert.ToDouble(brightness);
        if (config.TryGetValue("dynamicLightingColor", out var color))
            DynamicLightingColor = color as string;
        if (config.TryGetValue("dynamicLightingEffect", out var effect))
            DynamicLightingEffect = effect as string;
        if (config.TryGetValue("enableAutoRGB", out var autoRgb))
            EnableAutoRGB = Convert.ToBoolean(autoRgb);
        if (config.TryGetValue("autoRGBUpdateInterval", out var updateInterval))
            AutoRGBUpdateInterval = Convert.ToInt32(updateInterval);
    }
}
