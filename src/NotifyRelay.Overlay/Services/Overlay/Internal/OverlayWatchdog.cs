namespace NotifyRelay.Services.Overlay;

/// <summary>
/// 看门狗宿主契约：提供"故障隔离与自动恢复"所需的宿主能力。
/// 由 OverlayRenderService 显式实现，使看门狗不反向依赖渲染服务的具体实现。
/// </summary>
internal interface IOverlayWatchdogHost
{
    /// <summary>渲染循环运行标志；看门狗隔离时置 false 以停止旧循环。</summary>
    bool Running { get; set; }

    /// <summary>控制窗口句柄（用于探测渲染线程消息泵是否仍响应）。</summary>
    IntPtr ControlHandle { get; }

    /// <summary>当前所有覆盖层窗口（隔离时用于清除置顶）。</summary>
    IReadOnlyList<ScreenOverlay> Overlays { get; }

    /// <summary>渲染线程句柄；看门狗 Join 后置 null，避免与重启后的新线程混淆。</summary>
    Thread? RenderThread { get; set; }

    /// <summary>请求恢复清理：仅置位标志，实际清理（Win32.DestroyWindow / OverlayWindowManager.Cleanup）
    /// 交由重启后的渲染线程在启动时执行，避免看门狗线程直接操作窗口句柄。</summary>
    void RequestRecoveryCleanup();

    /// <summary>重启渲染循环。</summary>
    void Restart();
}

/// <summary>
/// 渲染线程心跳 + 看门狗（故障隔离）：渲染循环每帧更新心跳，
/// 看门狗线程检测心跳超时后清除置顶并自动恢复，避免覆盖层卡死拖死整个应用。
/// </summary>
internal sealed class OverlayWatchdog
{
    public const int WatchdogIntervalMs = 2000;     // 看门狗轮询间隔
    public const int HeartbeatTimeoutMs = 10000;    // 心跳超时阈值（判定卡死）
    public const int WindowProbeTimeoutMs = 800;    // 窗口响应探测超时

    private readonly ILogger _logger;
    private readonly IOverlayWatchdogHost _host;

    private long _lastHeartbeatTick;
    private Thread? _watchdogThread;

    public OverlayWatchdog(ILogger logger, IOverlayWatchdogHost host)
    {
        _logger = logger;
        _host = host;
    }

    /// <summary>渲染线程每帧调用：更新心跳时间戳。</summary>
    public void UpdateHeartbeat()
        => Interlocked.Exchange(ref _lastHeartbeatTick, Stopwatch.GetTimestamp());

    /// <summary>启动看门狗线程（幂等：线程仍存活时不重复创建）。</summary>
    public void EnsureStarted()
    {
        if (_watchdogThread != null && _watchdogThread.IsAlive) return;
        _watchdogThread = new Thread(WatchdogLoop) { IsBackground = true, Name = "D2D-Overlay-Watchdog" };
        _watchdogThread.Start();
    }

    private void WatchdogLoop()
    {
        while (true)
        {
            Thread.Sleep(WatchdogIntervalMs);
            if (!_host.Running) continue;   // 正常停止中，跳过
            long last = Interlocked.Read(ref _lastHeartbeatTick);
            if (last == 0) continue;
            long elapsedMs = (Stopwatch.GetTimestamp() - last) * 1000 / Stopwatch.Frequency;
            if (elapsedMs > HeartbeatTimeoutMs)
                RecoverFromHang(elapsedMs);
        }
    }

    /// <summary>
    /// 覆盖层卡死故障隔离：
    /// 1) 停止渲染循环；2) 清除所有覆盖层窗口 TOPMOST（不依赖窗口消息泵，立即恢复系统可操作性）；
    /// 3) 等待旧渲染线程退出（在本线程内 Join，不阻塞业务线程）；4) 确认退出后清理残留窗口并自动重启。
    /// 若渲染线程永久无法退出，则保持隔离状态（窗口已不再置顶），覆盖层停止工作但不影响系统。
    /// </summary>
    private void RecoverFromHang(long elapsedMs)
    {
        OverlayCrashLog.Write($"覆盖层渲染线程疑似卡死 {elapsedMs}ms，执行故障隔离与自动恢复");
        _logger.LogWarning("覆盖层渲染线程疑似卡死 {ElapsedMs}ms，执行故障隔离与自动恢复", elapsedMs);

        _host.Running = false;

        // 探测渲染线程消息泵是否仍响应（SMTO_ABORTIFHUNG 超时立即返回，不阻塞看门狗线程）
        if (_host.ControlHandle != IntPtr.Zero)
            Win32.SendMessageTimeoutW(_host.ControlHandle, Win32.WM_NULL, IntPtr.Zero, IntPtr.Zero,
                Win32.SMTO_ABORTIFHUNG, WindowProbeTimeoutMs, out _);

        // 兜底清除置顶：SetWindowLongPtr 不依赖窗口消息泵，即使窗口线程卡死也立即生效
        OverlayTopmostMonitor.ClearTopmost(_host.Overlays);

        // 等待旧渲染线程退出（卡死的线程恢复后会在 finally 中自毁窗口）
        var dead = _host.RenderThread;
        _host.RenderThread = null;
        bool exited = dead == null;
        for (int i = 0; i < 20 && !exited; i++)
        {
            dead!.Join(500);
            exited = !dead.IsAlive;
        }

        if (exited)
        {
            // 旧线程可能未执行 finally：向渲染线程投递清理请求（不直接操作窗口句柄），
            // 由重启后的新渲染线程在启动时执行残留资源清理并重建
            _host.RequestRecoveryCleanup();
            _host.Restart();
            _logger.LogWarning("覆盖层渲染线程已恢复并重启");
        }
        else
        {
            _logger.LogError("覆盖层渲染线程永久卡死无法退出，已隔离（清除置顶），覆盖层停止工作，请重启应用恢复");
        }
    }
}
