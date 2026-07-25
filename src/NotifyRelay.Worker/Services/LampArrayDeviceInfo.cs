using Windows.Devices.Lights;

namespace NotifyRelay.Worker.Services;

public class LampArrayDeviceInfo
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public LampArray? LampArray { get; set; }
    public bool IsAvailable { get; set; }
    public int LampCount { get; set; }
    public LampArrayKind Kind { get; set; }
}
