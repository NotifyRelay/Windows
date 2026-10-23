using NotifyRelay.Worker.Services;

namespace NotifyRelay.Worker.Contracts;

/// <summary>DeepSeek 余额查询事件参数。</summary>
public sealed class BalanceUpdatedEventArgs : EventArgs
{
    public BalanceUpdatedEventArgs(double balance) => Balance = balance;

    /// <summary>最新余额。</summary>
    public double Balance { get; }
}

/// <summary>DeepSeek 余额轮询工具契约：命令 + 状态快照 + 事件。</summary>
public interface IDeepSeekBalanceTool
{
    /// <summary>是否正在轮询。</summary>
    bool IsPolling { get; }

    /// <summary>当前余额。</summary>
    double CurrentBalance { get; }

    /// <summary>余额更新。</summary>
    event EventHandler<BalanceUpdatedEventArgs>? BalanceUpdated;

    /// <summary>运行状态变化。</summary>
    event EventHandler? StatusChanged;

    /// <summary>启动轮询（未配置 Token 时不启动）。</summary>
    void StartPolling();

    /// <summary>停止轮询。</summary>
    void StopPolling();

    /// <summary>立即查询一次余额。</summary>
    Task<double?> FetchBalanceAsync();

    /// <summary>清空历史记录并落盘。</summary>
    void ClearHistory();

    /// <summary>余额历史快照（时间升序）。</summary>
    List<BalanceHistoryItem> GetHistorySnapshot();
}
