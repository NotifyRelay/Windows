namespace NotifyRelay.DeviceCtrl.VirtualSpeaker;

public class DlnaRendererInfo
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Location { get; set; } = string.Empty;
    public string IpAddress { get; set; } = string.Empty;
    public int Port { get; set; }
    public string ControlUrl { get; set; } = string.Empty;
    public string AvTransportUrl { get; set; } = string.Empty;
    public string RenderingControlUrl { get; set; } = string.Empty;
}
