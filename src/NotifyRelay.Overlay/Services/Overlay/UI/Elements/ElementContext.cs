using Microsoft.Extensions.Logging;
using Vortice.Direct2D1;
using Vortice.DirectWrite;

namespace NotifyRelay.Services.Overlay.UI;

/// <summary>
/// 叠加层元素的共享环境：设置访问、工厂、状态锁、窗口集合访问器、脏标志通知。
/// 由 <see cref="OverlayRenderService"/> 在构造时创建一次，5 个元素共用。
/// </summary>
internal sealed class ElementContext
{
    /// <summary>有界等待超时（毫秒），与现有实现保持一致。</summary>
    private const int LockTimeoutMs = 2000;

    public ElementContext(IOverlaySettings settings, ILogger logger,
        ID2D1Factory d2dFactory, IDWriteFactory dwFactory,
        object stateLock,
        Func<IReadOnlyList<ScreenOverlay>> overlays,
        Func<ScreenOverlay?> spanOverlay,
        Action markDirty)
    {
        Settings = settings;
        Logger = logger;
        D2DFactory = d2dFactory;
        DwFactory = dwFactory;
        StateLock = stateLock;
        Overlays = overlays;
        SpanOverlay = spanOverlay;
        MarkDirty = markDirty;
    }

    public IOverlaySettings Settings { get; }

    public ILogger Logger { get; }

    public ID2D1Factory D2DFactory { get; }

    public IDWriteFactory DwFactory { get; }

    /// <summary>元素状态锁（渲染线程与业务线程共用，语义同现有 _lock）。</summary>
    public object StateLock { get; }

    /// <summary>当前全部覆盖层窗口（渲染线程维护，读取无需加锁）。</summary>
    public Func<IReadOnlyList<ScreenOverlay>> Overlays { get; }

    /// <summary>跨屏窗口（跨屏模式下存在，否则为 null）。</summary>
    public Func<ScreenOverlay?> SpanOverlay { get; }

    /// <summary>置位显示脏标志，触发渲染线程重新同步窗口集合。</summary>
    public Action MarkDirty { get; }

    /// <summary>
    /// 以 <see cref="Monitor.TryEnter(object, int)"/> 有界等待方式加锁执行；拿不到锁时记录并跳过本次更新。
    /// 复刻现有 10 处「TryEnter(2000) + 日志 + try/finally Exit」样板，彻底消除重复。
    /// </summary>
    public bool WithLock(Action body, string skipMessage)
    {
        if (!Monitor.TryEnter(StateLock, LockTimeoutMs))
        {
            Logger.LogWarning("{Message}", skipMessage);
            return false;
        }
        try
        {
            body();
            return true;
        }
        finally
        {
            Monitor.Exit(StateLock);
        }
    }

    /// <summary>加锁求值；拿不到锁时返回 <paramref name="fallback"/>（不记录日志，用于每帧判定热路径）。</summary>
    public T WithLock<T>(Func<T> body, T fallback)
    {
        if (!Monitor.TryEnter(StateLock, LockTimeoutMs)) return fallback;
        try
        {
            return body();
        }
        finally
        {
            Monitor.Exit(StateLock);
        }
    }

    /// <summary>目标屏判定：复用共用核心（primary / 设备名精确匹配 / 回退主屏）。</summary>
    public bool IsTargetScreen(ScreenOverlay o, string? target, bool allowSpan)
        => OverlayElementCore.IsTargetScreen(o, target, Overlays(), SpanOverlay(), allowSpan);

    /// <summary>按百分比解析锚点坐标（元素中心基准，与旧 ResolveAnchor 等价）。</summary>
    public static (float X, float Y) ResolveAnchor(ScreenOverlay o, float xPercent, float yPercent)
        => OverlayElementCore.ResolveAnchor(o, xPercent, yPercent);

    /// <summary>解析元素整体缩放系数。</summary>
    public static float ResolveScale(float rawScale, float min, float max)
        => OverlayElementCore.ResolveScale(rawScale, min, max);
}
