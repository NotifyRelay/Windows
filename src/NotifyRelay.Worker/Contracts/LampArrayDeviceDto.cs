namespace NotifyRelay.Worker.Contracts;

/// <summary>灯效设备快照。</summary>
public class LampArrayDeviceDto
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public bool IsAvailable { get; set; }
    public int LampCount { get; set; }
    public string Kind { get; set; } = string.Empty;
}
