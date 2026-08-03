namespace NotifyRelay.Worker.Configuration;

public class WorkerConfiguration
{
    public string? ControlMyMonitorPath { get; set; }
    public List<string> SelectedMonitors { get; set; } = [];
    public bool EnableMonitorBrightnessSync { get; set; }

    public double DynamicLightingBrightness { get; set; } = 1.0;
    public string? DynamicLightingColor { get; set; }
    public string? DynamicLightingEffect { get; set; }
    public bool EnableAutoRGB { get; set; }
    public int AutoRGBUpdateInterval { get; set; } = 5000;


}
