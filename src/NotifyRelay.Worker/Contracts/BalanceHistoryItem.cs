namespace NotifyRelay.Worker.Contracts;

/// <summary>DeepSeek 余额历史记录条目。</summary>
public class BalanceHistoryItem
{
    public DateTime Time { get; set; }
    public double Balance { get; set; }
    public double Change { get; set; }
    public string ChangeType { get; set; } = string.Empty;
    public int MergeCount { get; set; } = 1;
}
