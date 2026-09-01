using System.Runtime.InteropServices;

namespace NotifyRelay.Services.Overlay;

/// <summary>
/// 覆盖层模块的控制窗口：注册共享窗口类、承载 1x1 隐藏窗口与 WndProc 消息泵定时器。
/// 生命周期贯穿服务，不被多屏重建影响；覆盖层窗口与其共用同一窗口类，从而共用 WndProc。
/// 显示变化（WM_DISPLAYCHANGE）与置顶重断言到期（WM_TIMER）通过回调抛给宿主，避免反向依赖渲染服务。
/// </summary>
internal sealed class OverlayControlWindow
{
    private readonly Action _onDisplayChanged;
    private readonly Action _onReassertTopmost;

    private Win32.WndProcDelegate? _wndProcDelegate;
    private bool _classRegistered;

    public OverlayControlWindow(Action onDisplayChanged, Action onReassertTopmost)
    {
        _onDisplayChanged = onDisplayChanged;
        _onReassertTopmost = onReassertTopmost;
    }

    /// <summary>控制窗口句柄；未创建时为 <see cref="IntPtr.Zero"/>。</summary>
    public IntPtr Handle { get; private set; }

    /// <summary>注册覆盖层窗口类（幂等）。必须在创建任何窗口之前调用。</summary>
    public void EnsureWindowClass()
    {
        if (_classRegistered) return;
        _wndProcDelegate = WndProc;
        var hInstance = Win32.GetModuleHandleW(null);
        var wc = new Win32.WNDCLASSW
        {
            lpfnWndProc = Marshal.GetFunctionPointerForDelegate(_wndProcDelegate),
            hInstance = hInstance,
            lpszClassName = Win32.OverlayWindowClass
        };
        Win32.RegisterClassW(ref wc);
        _classRegistered = true;
    }

    /// <summary>创建 1x1 的隐藏控制窗口（复用同一窗口类，从而共用 WndProc）。</summary>
    public void Create()
    {
        if (Handle != IntPtr.Zero) return;
        var hInstance = Win32.GetModuleHandleW(null);
        Handle = Win32.CreateWindowExW(
            0, Win32.OverlayWindowClass, "NotifyRelayOverlayCtrl",
            Win32.WS_POPUP, 0, 0, 1, 1,
            IntPtr.Zero, IntPtr.Zero, hInstance, IntPtr.Zero);
    }

    /// <summary>销毁控制窗口并清理挂起的定时器。</summary>
    public void Destroy()
    {
        if (Handle == IntPtr.Zero) return;
        Win32.KillTimer(Handle, Win32.REASSERT_TIMER_ID);
        Win32.DestroyWindow(Handle);
        Handle = IntPtr.Zero;
    }

    /// <summary>
    /// 预约一次延迟的 TOPMOST 重断言（由 WndProc 在 WM_TIMER 中执行）。
    /// 防抖：短时间内重复触发时重置计时器，仅最后一次触发后延迟一次刷新。
    /// 仅由显示模式变化（WM_DISPLAYCHANGE）与覆盖层重新显示时触发，不再监听前台窗口变更，
    /// 避免切换窗口（如打开任务管理器）时覆盖层抢占层级导致系统窗口无法操作。
    /// </summary>
    public void ScheduleReassertTopmost()
    {
        if (Handle == IntPtr.Zero) return;
        Win32.KillTimer(Handle, Win32.REASSERT_TIMER_ID);
        Win32.SetTimer(Handle, Win32.REASSERT_TIMER_ID, Win32.REASSERT_DELAY_MS, IntPtr.Zero);
    }

    private IntPtr WndProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        if (msg == Win32.WM_DISPLAYCHANGE)
        {
            // 显示模式切换（如独占全屏游戏退出）：通知宿主触发几何同步，并预约延迟重断言 TOPMOST
            _onDisplayChanged();
            ScheduleReassertTopmost();
        }
        else if (msg == Win32.WM_TIMER && wParam == (uint)Win32.REASSERT_TIMER_ID)
        {
            // 延迟任务到期：在本渲染线程(WndProc)中真正执行窗口 z-order 重断言
            Win32.KillTimer(hWnd, Win32.REASSERT_TIMER_ID);
            _onReassertTopmost();
        }
        return Win32.DefWindowProcW(hWnd, msg, wParam, lParam);
    }
}
