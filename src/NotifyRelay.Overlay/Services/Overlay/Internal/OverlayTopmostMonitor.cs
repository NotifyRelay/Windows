namespace NotifyRelay.Services.Overlay;

/// <summary>
/// 置顶监控：WinEvent 钩子（前台/焦点变化） + 渲染循环周期巡检兜底，
/// 检测覆盖层被系统/其他应用重置 z-order 后自动恢复置顶（参考 PowerToys AlwaysOnTop 的可靠性机制）。
/// 钩子回调发生在注册它的线程（渲染线程），因此本类的安装/卸载也须在渲染线程执行。
/// </summary>
internal sealed class OverlayTopmostMonitor
{
    private static readonly uint[] TopmostMonitorEvents = { Win32.EVENT_OBJECT_FOCUS, Win32.EVENT_SYSTEM_FOREGROUND };

    private readonly ILogger _logger;
    private readonly List<IntPtr> _hooks = [];
    private Win32.WinEventDelegate? _winEventProcDelegate;

    public OverlayTopmostMonitor(ILogger logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// 注册置顶监控 WinEvent 钩子（渲染线程调用，回调也在渲染线程执行）。
    /// 每次调用先清理残留钩子再重建，保证渲染线程重启后监控仍然有效。
    /// </summary>
    /// <param name="onWinEvent">前台/焦点变化回调，宿主在此检查并恢复覆盖层置顶。</param>
    public void Install(Action onWinEvent)
    {
        Uninstall();
        _winEventProcDelegate = (_, _, _, _, _, _, _) => onWinEvent();
        foreach (var evt in TopmostMonitorEvents)
        {
            var hook = Win32.SetWinEventHook(evt, evt, IntPtr.Zero, _winEventProcDelegate, 0, 0,
                Win32.WINEVENT_OUTOFCONTEXT | Win32.WINEVENT_SKIPOWNPROCESS);
            if (hook != IntPtr.Zero)
                _hooks.Add(hook);
        }
        if (_hooks.Count == 0)
            _logger.LogWarning("注册置顶监控 WinEvent 钩子失败，仅依赖渲染循环周期巡检");
    }

    public void Uninstall()
    {
        foreach (var hook in _hooks)
            Win32.UnhookWinEvent(hook);
        _hooks.Clear();
        _winEventProcDelegate = null;
    }

    /// <summary>检查所有可见覆盖层窗口是否保持 WS_EX_TOPMOST，被重置则恢复（须在渲染线程调用）。</summary>
    public static void EnsureOverlaysTopmost(IReadOnlyList<ScreenOverlay> overlays)
    {
        for (int i = 0; i < overlays.Count; i++)
        {
            var o = overlays[i];
            if (!o.Visible || o.Hwnd == IntPtr.Zero || !Win32.IsWindow(o.Hwnd)) continue;
            EnsureTopmost(o.Hwnd);
        }
    }

    /// <summary>
    /// 周期巡检无条件重断言所有可见覆盖层窗口的 TOPMOST z-order。
    /// 处理"WS_EX_TOPMOST 标志仍在但 z-order 被系统/游戏重建压到普通窗口之下"的失效场景
    /// （全屏游戏退出等，参考 PowerToys #17332）。低频（约每秒一次）执行，无移动/缩放/激活副作用。
    /// </summary>
    public static void ReassertOverlaysTopmost(IReadOnlyList<ScreenOverlay> overlays)
    {
        for (int i = 0; i < overlays.Count; i++)
        {
            var o = overlays[i];
            if (!o.Visible || o.Hwnd == IntPtr.Zero || !Win32.IsWindow(o.Hwnd)) continue;
            ReassertTopmostCore(o.Hwnd);
        }
    }

    /// <summary>
    /// 兜底清除置顶：SetWindowLongPtr 不依赖窗口消息泵，即使窗口线程卡死也立即生效
    /// （看门狗隔离卡死渲染线程时使用）。
    /// </summary>
    public static void ClearTopmost(IReadOnlyList<ScreenOverlay> overlays)
    {
        for (int i = 0; i < overlays.Count; i++)
        {
            var o = overlays[i];
            if (o.Hwnd == IntPtr.Zero || !Win32.IsWindow(o.Hwnd)) continue;
            long exStyle = Win32.GetWindowLongPtr(o.Hwnd, Win32.GWL_EXSTYLE).ToInt64();
            if ((exStyle & Win32.WS_EX_TOPMOST) != 0)
                Win32.SetWindowLongPtr(o.Hwnd, Win32.GWL_EXSTYLE, new IntPtr(exStyle & ~Win32.WS_EX_TOPMOST));
        }
    }

    /// <summary>单个窗口置顶恢复：仅当 WS_EX_TOPMOST 丢失时 SetWindowPos，无移动/缩放/激活副作用，不引起闪烁。</summary>
    public static void EnsureTopmost(IntPtr hwnd)
    {
        long exStyle = Win32.GetWindowLongPtr(hwnd, Win32.GWL_EXSTYLE).ToInt64();
        if ((exStyle & Win32.WS_EX_TOPMOST) == 0)
            ReassertTopmostCore(hwnd);
    }

    private static void ReassertTopmostCore(IntPtr hwnd)
        => Win32.SetWindowPos(hwnd, Win32.HWND_TOPMOST, 0, 0, 0, 0,
            Win32.SWP_NOMOVE | Win32.SWP_NOSIZE | Win32.SWP_NOACTIVATE);
}
