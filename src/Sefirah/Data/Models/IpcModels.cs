namespace NotifyRelay.Data.Models;

public class BalanceHistoryItem
{
    public DateTime Time { get; set; }
    public double Balance { get; set; }
    public double Change { get; set; }
    public string ChangeType { get; set; } = string.Empty;
    public int MergeCount { get; set; } = 1;
}

public class MonitorInfo
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string DeviceName { get; set; } = string.Empty;
}

public class LampArrayDeviceInfo
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public bool IsAvailable { get; set; }
    public int LampCount { get; set; }
    public string Kind { get; set; } = string.Empty;
}
