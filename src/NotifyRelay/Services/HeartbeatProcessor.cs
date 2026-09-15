namespace NotifyRelay.Services;

/// <summary>
/// TCP 心跳（<c>HEARTBEAT_TCP</c>）处理。
///
/// 运行时状态（名称 / 电量 / 在线 / IP / 设备类型）<b>全部由 Rust core 的 DeviceRegistry 维护</b>，
/// 平台端不再镜像；本类只负责「按最小间隔触发一次快照刷新」，
/// 避免高频心跳引发 FFI 调用风暴。
/// </summary>
public class HeartbeatProcessor
{
    /// <summary>相邻两次快照刷新的最小间隔</summary>
    private const long RefreshMinIntervalMs = 500;

    private long lastRefreshAt;

    /// <summary>
    /// 设备状态变化通知（由 Rust 回调触发，如 TCP 扫描发现、设备超时）。
    /// 设备状态（在线/离线/名称/IP/电量/是否可见）完全由 Rust core 负责，
    /// 平台端不解析具体字段，仅据此重新拉取 core 设备快照后刷新 UI。
    /// </summary>
    public event Action? DeviceListChanged;

    /// <summary>
    /// 触发设备列表刷新（可能运行在 Rust 回调线程，订阅方需自行保证不在 core 回调栈内同步重入）。
    /// 心跳为高频事件，此处按最小间隔节流。
    /// </summary>
    public void NotifyDeviceListChanged()
    {
        var now = Environment.TickCount64;
        var prev = Interlocked.Read(ref lastRefreshAt);
        if (now - prev < RefreshMinIntervalMs) return;
        if (Interlocked.CompareExchange(ref lastRefreshAt, now, prev) != prev) return;

        DeviceListChanged?.Invoke();
    }
}



