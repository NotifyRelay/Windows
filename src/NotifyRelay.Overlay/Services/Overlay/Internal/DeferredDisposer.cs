using System.Collections.Concurrent;

namespace NotifyRelay.Services.Overlay;

/// <summary>
/// 延迟释放队列：D2D/DWrite 对象只在渲染线程释放。
/// 业务线程（媒体/BLE 推送等）一律入队，由渲染线程每帧开头统一释放，
/// 避免绘制期间并发释放导致本机崩溃（ExecutionEngineException）。
/// </summary>
internal sealed class DeferredDisposer
{
    private readonly ConcurrentQueue<IDisposable> _released = new();
    private readonly ILogger _logger;

    public DeferredDisposer(ILogger logger)
    {
        _logger = logger;
    }

    /// <summary>业务线程调用：仅入队，不直接释放。</summary>
    public void Enqueue(IDisposable? d)
    {
        if (d != null) _released.Enqueue(d);
    }

    /// <summary>渲染线程调用：释放业务线程入队的对象（仅在渲染线程执行释放）。</summary>
    public void Flush()
    {
        while (_released.TryDequeue(out var d))
        {
            try { d.Dispose(); }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "延迟释放覆盖层资源失败");
            }
        }
    }
}
