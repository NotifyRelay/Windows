using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using NotifyRelay.Models.Render;
using Vortice;
using Vortice.Direct2D1;
using Vortice.DirectWrite;
using Vortice.Mathematics;
using Vortice.WIC;
using static NotifyRelay.Services.Overlay.Win32;

namespace NotifyRelay.Services.Overlay;

public sealed partial class OverlayRenderService : IDisposable, IOverlayWatchdogHost
{
    private readonly ILogger<OverlayRenderService> _logger;
    private readonly IOverlaySettings _settings;
    private readonly ID2D1Factory _d2dFactory;
    private readonly IDWriteFactory _dwFactory;
    private readonly IWICImagingFactory _wicFactory;

    // ===== 管理单元 =====
    // 窗口/渲染核心、控制窗口、置顶监控、看门狗、延迟释放各自内聚，
    // 本服务只保留渲染循环与生命周期协调；新增叠加层元素直接复用这些单元。
    private readonly OverlayWindowManager _windowManager;
    private readonly OverlayControlWindow _controlWindow;
    private readonly OverlayTopmostMonitor _topmostMonitor;
    private readonly OverlayWatchdog _watchdog;
    private readonly DeferredDisposer _disposer;

    // 叠加层功能自动引导器（功能由 DI 注入，主项目只登记实现，此处按开关自动启停）
    private readonly OverlayFeatureStartup _featureStartup;

    private Thread? _renderThread;
    private volatile int _runningFlag;   // 0=未启动/已停止, 1=运行中（原子争用启动所有权，供看门狗/并发调用安全）
    // 恢复清理挂起标志：看门狗检测到卡死并确认旧线程退出后置位，
    // 由新启动的渲染线程在创建窗口前执行残留资源清理（保证窗口操作线程亲和）。
    private volatile bool _recoveryCleanupPending;

    private int _consecutiveRenderFails;
    private const int MaxConsecutiveRenderFails = 60;

    // 顶部卡片（媒体 + SuperIsland），由 _lock 保护
    private readonly List<OverlayItem> _topItems = [];
    private readonly object _lock = new();

    // 弹幕请求队列（跨线程），由渲染线程分发到各屏覆盖层
    private readonly ConcurrentQueue<DanmakuRequest> _requests = new();

    private DanmakuStyleSettings _currentStyle = new();
    private volatile int _maxFps;         // 0 = 跟随刷新率(DwmFlush)；否则为帧率上限
    private int _screenMode;              // 当前多屏模式，变化时触发覆盖层重建
    private volatile bool _displayDirty;
    private readonly Random _rand = new();

    // 快捷键映射触发时的提示文本（由钩子线程推送，渲染线程消费，受 _lock 保护）
    private string? _keyMappingHintText;
    private long _keyMappingHintTick;     // Stopwatch 时间戳
    private const int KeyMappingHintTimeoutMs = 1500;

    public OverlayRenderService(ILogger<OverlayRenderService> logger, IOverlaySettings settings,
        IEnumerable<IOverlayFeature>? features = null)
    {
        _logger = logger;
        _settings = settings;
        _d2dFactory = D2D1.D2D1CreateFactory<ID2D1Factory>();
        _dwFactory = DWrite.DWriteCreateFactory<IDWriteFactory>();
        _wicFactory = new IWICImagingFactory();

        _featureStartup = new OverlayFeatureStartup(features, logger);
        _windowManager = new OverlayWindowManager(_d2dFactory, logger);
        _controlWindow = new OverlayControlWindow(
            // 显示模式切换：置脏以触发几何同步（重断言 TOPMOST 由控制窗口内部预约）
            onDisplayChanged: () => _displayDirty = true,
            onReassertTopmost: ReassertTopmost);
        _topmostMonitor = new OverlayTopmostMonitor(logger);
        _watchdog = new OverlayWatchdog(logger, this);
        _disposer = new DeferredDisposer(logger);
    }

    /// <summary>弹幕请求（业务线程入队，渲染线程分发）。</summary>
    private sealed class DanmakuRequest
    {
        public string Text = string.Empty;
        public byte[]? IconPng;
        public DanmakuStyleSettings Settings = new();
    }

    /// <summary>
    /// 叠加层模块主初始化：载入设置、自动引导各叠加层功能、启动渲染线程与看门狗。
    /// 调用方只需调用本方法，无需再逐条注册/启动键盘、电池、心率等功能。
    /// </summary>
    public void Start()
    {
        // 原子争用启动所有权：仅当仍处未启动态(0)时置为运行态(1)，
        // 其他线程已拥有启动则直接返回，避免并发 Start 启动多个渲染线程。
        if (Interlocked.CompareExchange(ref _runningFlag, 1, 0) != 0)
            return;
        // 启动时从已保存设置初始化样式，避免首条弹幕使用默认值（需手动调节才生效）
        LoadInitialStyle();
        LoadInitialHeartRateConfig();
        LoadInitialClockConfig();
        // 自动引导：按各自开关绑定并启动所有已登记的叠加层功能
        _featureStartup.Initialize(this);
        _watchdog.UpdateHeartbeat();
        _renderThread = new Thread(RenderLoop) { IsBackground = true, Name = "D2D-Overlay" };
        _renderThread.SetApartmentState(ApartmentState.STA);
        _renderThread.Start();
        _watchdog.EnsureStarted();
    }

    /// <summary>从已保存的设置构建初始样式并写入 _currentStyle / _maxFps。</summary>
    private void LoadInitialStyle()
    {
        var s = _settings;
        var style = new DanmakuStyleSettings
        {
            FontSizePercent = s.DanmakuFontSizePercent,
            Speed = s.DanmakuSpeed,
            OpacityPercent = s.DanmakuOpacityPercent,
            DisplayAreaPercent = s.DanmakuDisplayAreaPercent,
            Density = s.DanmakuDensity,
            FontFamilyName = s.DanmakuFontFamily,
            Bold = s.DanmakuBold,
            ColorR = ParseColorChannel(s.DanmakuColor, 255, 0),
            ColorG = ParseColorChannel(s.DanmakuColor, 255, 2),
            ColorB = ParseColorChannel(s.DanmakuColor, 255, 4),
            BorderEnabled = s.DanmakuBorderEnabled,
            BorderThickness = s.DanmakuBorderThickness,
            BorderColorR = ParseColorChannel(s.DanmakuBorderColor, 0, 0),
            BorderColorG = ParseColorChannel(s.DanmakuBorderColor, 0, 2),
            BorderColorB = ParseColorChannel(s.DanmakuBorderColor, 0, 4),
            ShadowEnabled = s.DanmakuShadowEnabled,
            ShadowDepth = s.DanmakuShadowDepth,
            ShadowOpacity = s.DanmakuShadowOpacity,
            ShadowColorR = ParseColorChannel(s.DanmakuShadowColor, 0, 0),
            ShadowColorG = ParseColorChannel(s.DanmakuShadowColor, 0, 2),
            ShadowColorB = ParseColorChannel(s.DanmakuShadowColor, 0, 4),
            DisplayScreenMode = s.DanmakuDisplayScreenMode,
            PerformanceMode = s.DanmakuPerformanceMode
        };
        lock (_lock)
        {
            _currentStyle = style;
            _maxFps = style.PerformanceMode switch
            {
                1 => 60,
                2 => 30,
                _ => 0
            };
        }
    }

    private static byte ParseColorChannel(string? hex, byte fallback, int offset)
    {
        if (string.IsNullOrEmpty(hex) || !hex.StartsWith("#")) return fallback;
        hex = hex.TrimStart('#');
        if (hex.Length != 6) return fallback;
        try { return byte.Parse(hex.Substring(offset, 2), System.Globalization.NumberStyles.HexNumber); }
        catch { return fallback; }
    }

    public void Stop()
    {
        Interlocked.Exchange(ref _runningFlag, 0);
        _renderThread?.Join(2000);
        _featureStartup.Shutdown();
        CleanupOverlays();
    }

    private void RenderLoop()
    {
        timeBeginPeriod(1);
        try
        {
            // 恢复场景：旧线程退出后残留窗口/DC 可能未被回收，必须在渲染线程（窗口创建线程）执行清理，
            // 清理成功、句柄状态归零后再由下方流程重建窗口，避免跨线程 DestroyWindow 引发句柄状态错乱。
            if (_recoveryCleanupPending)
            {
                _recoveryCleanupPending = false;
                PerformRecoveryCleanup();
            }

            _controlWindow.EnsureWindowClass();
            _topmostMonitor.Install(EnsureOverlaysTopmost);
            SyncOverlays();
            _controlWindow.Create();

            var timer = Stopwatch.StartNew();
            double nextFrame = 0;
            int frameCount = 0;
            bool anyVisible = false;

            while (_runningFlag != 0)
            {
                while (PeekMessageW(out var msg, IntPtr.Zero, 0, 0, 1))
                {
                    TranslateMessage(ref msg);
                    DispatchMessageW(ref msg);
                }

                // 渲染线程心跳：供看门狗检测卡死（消息泵或渲染卡住都会停止更新）
                _watchdog.UpdateHeartbeat();

                // 释放业务线程延迟入队的 D2D/DWrite 对象（锁外，绘制前统一执行）
                _disposer.Flush();

                // 定期检测显示器热插拔 / 模式变更
                if ((frameCount++ & 63) == 0 || _displayDirty)
                {
                    _displayDirty = false;
                    SyncOverlays();
                    // 周期巡检兜底：无条件重断言 TOPMOST 层级（约每秒一次，成本极低）。
                    // 覆盖"标志仍在但 z-order 被全屏游戏/系统重建压到普通窗口之下"的失效场景，
                    // 以及 WinEvent 钩子漏触发的置顶重置。
                    OverlayTopmostMonitor.ReassertOverlaysTopmost(_windowManager.Overlays);
                }

                DispatchRequests();

                bool hasContent = TopItemsActive();
                if (!hasContent)
                {
                    foreach (var o in _windowManager.Overlays)
                        if (o.Items.Count > 0 || o.Pending.Count > 0) { hasContent = true; break; }
                }
                if (!hasContent && !_requests.IsEmpty) hasContent = true;
                if (!hasContent) hasContent = HeartRateActive();
                // 罗技电池独立驱动：只看电量自身条件，不受弹幕/心率/键盘等其他叠加层元素影响
                if (!hasContent) hasContent = LogiBatteryActive();
                if (!hasContent) hasContent = ClockActive();
                if (!hasContent && _settings.KeyboardOverlayEnabled
                    && _keyboardStateProvider != null && _keyboardStateProvider.GetPressedKeys().Any())
                    hasContent = true;
                if (!hasContent && HasKeyMappingHint())
                    hasContent = true;

                if (!hasContent)
                {
                    // 空闲：隐藏所有窗口并降频休眠，避免占用游戏帧率
                    if (anyVisible)
                    {
                        foreach (var o in _windowManager.Overlays)
                            if (o.Visible) { ShowWindow(o.Hwnd, SW_HIDE); o.Visible = false; }
                        anyVisible = false;
                    }
                    Thread.Sleep(30);
                    continue;
                }

                // 帧率上限（均衡/游戏档）——通过休眠限帧
                int maxFps = _maxFps;
                if (maxFps > 0)
                {
                    double frameTime = 1.0 / maxFps;
                    double now = timer.Elapsed.TotalSeconds;
                    if (now < nextFrame)
                    {
                        int waitMs = (int)((nextFrame - now) * 1000);
                        if (waitMs > 1) Thread.Sleep(waitMs - 1);
                        while (timer.Elapsed.TotalSeconds < nextFrame) Thread.SpinWait(10);
                    }
                    nextFrame = timer.Elapsed.TotalSeconds + frameTime;
                }

                // 单帧渲染保护：失败时记录并继续，连续失败过多则退出循环交由看门狗恢复
                try
                {
                    double ts = Stopwatch.GetTimestamp();
                    double freq = Stopwatch.Frequency;
                    foreach (var o in _windowManager.Overlays)
                        RenderOverlay(o, ts, freq);
                    anyVisible = true;

                    // 流畅档：跟随显示器刷新率
                    if (maxFps <= 0) DwmFlush();
                    _consecutiveRenderFails = 0;
                }
                catch (Exception ex)
                {
                    _consecutiveRenderFails++;
                    if (_consecutiveRenderFails == 1 || _consecutiveRenderFails % 10 == 0)
                        OverlayCrashLog.Write($"覆盖层渲染帧失败 #{_consecutiveRenderFails}", ex);
                    if (_consecutiveRenderFails >= MaxConsecutiveRenderFails)
                    {
                        OverlayCrashLog.Write("覆盖层渲染连续失败次数过多，退出渲染循环，交由看门狗恢复");
                        _logger.LogError(ex, "OverlayRenderService 渲染连续失败 {Count} 次，退出渲染循环", _consecutiveRenderFails);
                        break;
                    }
                    Thread.Sleep(100);
                }
            }
        }
        catch (Exception ex)
        {
            OverlayCrashLog.Write("覆盖层渲染线程崩溃", ex);
            _logger.LogError(ex, "OverlayRenderService 渲染线程异常退出");
        }
        finally
        {
            _controlWindow.Destroy();
            _topmostMonitor.Uninstall();
            timeEndPeriod(1);
            // 渲染线程退出时销毁自身创建的窗口，避免全屏覆盖层滞留屏幕抢占鼠标/层级
            CleanupOverlays();
        }
    }

    /// <summary>
    /// 重新断言所有覆盖层窗口的 TOPMOST z-order。
    /// 显示模式切换（如独占全屏游戏退出）会导致系统重建窗口 z-order、
    /// 丢失创建时设置的顶层状态，需在切换后/重新显示时主动重设。
    /// NOMOVE|NOSIZE|NOACTIVATE 无移动/缩放/激活副作用，不会引起闪烁。
    /// </summary>
    private void ReassertTopmost()
    {
        foreach (var o in _windowManager.Overlays)
            if (o.Hwnd != IntPtr.Zero)
                SetWindowPos(o.Hwnd, HWND_TOPMOST, 0, 0, 0, 0,
                    SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
    }

    /// <summary>置顶监控回调：检查所有可见覆盖层是否保持 WS_EX_TOPMOST，被重置则恢复。</summary>
    private void EnsureOverlaysTopmost()
        => OverlayTopmostMonitor.EnsureOverlaysTopmost(_windowManager.Overlays);

    /// <summary>根据当前多屏模式与显示器布局，按需重建覆盖层窗口集合。</summary>
    private void SyncOverlays()
    {
        int mode;
        lock (_lock) mode = _currentStyle.DisplayScreenMode;
        _screenMode = mode;
        _windowManager.SyncOverlays(mode, InvalidateTopItemDeviceResources);
    }

    /// <summary>枚举所有显示器（使用完整屏幕区域，弹幕可跨越整屏宽度）。</summary>
    private static List<ScreenInfo> EnumerateScreens()
        => OverlayWindowManager.EnumerateScreens();

    /// <summary>销毁并清空所有覆盖层窗口与顶部卡片资源（渲染线程调用）。</summary>
    private void CleanupOverlays()
    {
        DisposeClockResources();
        _windowManager.Cleanup();
        lock (_lock)
        {
            foreach (var item in _topItems) item.Dispose();
            _topItems.Clear();
        }
    }

    /// <summary>
    /// 业务线程延迟释放 D2D/DWrite 对象：仅入队，不直接释放，
    /// 由渲染线程在安全时机（绘制前、对象已脱离快照）统一执行。
    /// </summary>
    private void DeferDispose(IDisposable? d)
        => _disposer.Enqueue(d);

    /// <summary>是否存在活跃的快捷键映射提示（未超时），用于 hasContent 判定。</summary>
    private bool HasKeyMappingHint()
    {
        if (_keyMappingHintText == null) return false;
        lock (_lock)
        {
            return _keyMappingHintText != null
                && (Stopwatch.GetTimestamp() - _keyMappingHintTick) * 1000 / Stopwatch.Frequency < KeyMappingHintTimeoutMs;
        }
    }

    /// <summary>渲染线程读取映射提示文本，并返回淡出不透明度；超时则清空。</summary>
    private string? GetKeyMappingHintForRender(out float opacity)
    {
        opacity = 0;
        if (_keyMappingHintText == null) return null;
        lock (_lock)
        {
            if (_keyMappingHintText == null) return null;
            long elapsedMs = (Stopwatch.GetTimestamp() - _keyMappingHintTick) * 1000 / Stopwatch.Frequency;
            if (elapsedMs >= KeyMappingHintTimeoutMs)
            {
                _keyMappingHintText = null;
                return null;
            }
            // 最后 30% 时长淡出
            float remaining = 1f - (float)elapsedMs / KeyMappingHintTimeoutMs;
            opacity = remaining < 0.3f ? remaining / 0.3f : 1f;
            return _keyMappingHintText;
        }
    }

    private bool TopItemsActive()
    {
        lock (_lock)
        {
            foreach (var it in _topItems)
                if (it.Active) return true;
        }
        return false;
    }

    private void RenderOverlay(ScreenOverlay o, double now, double freq)
    {
        var rt = o.RenderTarget;
        if (rt == null) return;

        bool isHeartRateTarget = IsHeartRateTarget(o);
        bool hasKeyboardContent = o.IsPrimary && (
            (_settings.KeyboardOverlayEnabled
                && _keyboardStateProvider != null && _keyboardStateProvider.GetPressedKeys().Any())
            || HasKeyMappingHint());

        // 罗技电池：目标屏判定统一复用 IsLogiBatteryTarget，
        // 与 RenderLoop 的 LogiBatteryActive 共用同一真源，避免两处实现漂移。
        // 此处只决定"该窗口是否保留"，不要求当前已有可绘制设备：
        // 同一窗口上可能还挂着心率/弹幕等内容，不能因为电量暂空就整窗隐藏；
        // 是否真的画出卡片由 RenderLogiBattery 内部按设备过滤决定。
        bool isLogiTargetScreen = IsLogiBatteryTarget(o);
        bool isClockTarget = IsClockTarget(o);

        // 时钟时间文本（仅时钟目标屏计算一次，用于变化检测）
        string? clockText = isClockTarget ? GetClockTimeText() : null;

        // 除时钟外的其他内容：决定本窗口是否仍需清除并重绘
        bool otherContent = o.Items.Count > 0 || o.Pending.Count > 0
            || (o.IsPrimary && TopItemsActive())
            || isHeartRateTarget
            || hasKeyboardContent
            || isLogiTargetScreen;  // ← 独立：不受其他叠加层元素控制
        bool hasContent = otherContent || isClockTarget;
        if (!hasContent)
        {
            if (o.Visible) { ShowWindow(o.Hwnd, SW_HIDE); o.Visible = false; }
            return;
        }

        // 时钟是否需要刷新：时间文本变化 / 渲染目标变化（缓存画刷失效） / 窗口尚未可见。
        // 仅时钟且无其他内容、本帧无需刷新时，保留窗口上一帧画面可见、跳过清除与重绘，避免每帧重绘。
        bool clockDirty = isClockTarget
            && (clockText != _clockCacheText || _clockCacheBrushRt != o.RenderTarget || !o.Visible);
        if (isClockTarget && !clockDirty && !otherContent)
        {
            return;   // 时钟窗口保持可见，不重绘
        }

        rt.BeginDraw();
        rt.Clear(new Color4(0, 0, 0, 0));

        if (o.IsPrimary)
            RenderTopCards(o, now, freq);
        else
            o.TopOffset = 0;

        SpawnPending(o, rt);

        for (int i = o.Items.Count - 1; i >= 0; i--)
        {
            var item = o.Items[i];
            double elapsed = (now - item.StartTime) / freq;
            double x = item.SpawnX - elapsed * item.Settings.PixelsPerSecond;
            if (x < -item.TotalWidth - 50)
            {
                item.Dispose();
                o.Items.RemoveAt(i);
                continue;
            }
            DrawDanmaku(item, (float)x, rt);
        }

        if (isHeartRateTarget)
            DrawHeartRate(o);

        // 渲染时间浮窗（无背景、仅描边，自由浮动）
        if (isClockTarget)
            DrawClock(o);

        // 渲染键盘按键状态（左上角）
        if (o.IsPrimary)
            RenderKeyboardState(o, now, freq);

        // 渲染罗技电池设备卡片（LogiBattery）
        RenderLogiBattery(o, now, freq);

        rt.EndDraw();

        var ptSrc = new POINT(0, 0);
        var size = new SIZE(o.Width, o.Height);
        var ptDst = new POINT(o.OriginX, o.OriginY);
        var blend = new BLENDFUNCTION { BlendOp = AC_SRC_OVER, BlendFlags = 0, SourceConstantAlpha = 255, AlphaFormat = AC_SRC_ALPHA };
        UpdateLayeredWindow(o.Hwnd, IntPtr.Zero, ref ptDst, ref size, o.MemDC, ref ptSrc, 0, ref blend, ULW_ALPHA);

        if (!o.Visible)
        {
            ShowWindow(o.Hwnd, SW_SHOWNOACTIVATE);
            // 重新显示时预约 TOPMOST 重断言，交由 UI 线程(WndProc)延迟执行，避免在后台渲染线程直接操作窗口
            _controlWindow.ScheduleReassertTopmost();
            o.Visible = true;
        }
    }

    private void DispatchRequests()
    {
        while (_requests.TryDequeue(out var req))
        {
            foreach (var o in SelectTargets(req.Settings.DisplayScreenMode))
            {
                var item = new DanmakuItem
                {
                    Text = req.Text,
                    IconPng = req.IconPng,
                    Settings = req.Settings
                };
                o.Pending.Enqueue(item);
            }
        }
    }

    private List<ScreenOverlay> SelectTargets(int mode)
    {
        var overlays = _windowManager.Overlays;
        var primary = _windowManager.PrimaryOverlay;
        if (overlays.Count == 0) return [];
        switch (mode)
        {
            case 1: // 所有屏幕
                {
                    var all = new List<ScreenOverlay>(overlays.Count);
                    for (int i = 0; i < overlays.Count; i++)
                        if (!overlays[i].IsSpan) all.Add(overlays[i]);
                    return all;
                }
            case 2: // 鼠标所在屏幕
                {
                    GetCursorPos(out var pt);
                    ScreenOverlay? hit = null;
                    for (int i = 0; i < overlays.Count; i++)
                    {
                        var o = overlays[i];
                        if (o.IsSpan) continue;
                        if (pt.X >= o.OriginX && pt.X < o.OriginX + o.Width
                            && pt.Y >= o.OriginY && pt.Y < o.OriginY + o.Height)
                        {
                            hit = o;
                            break;
                        }
                    }
                    return [hit ?? primary ?? overlays[0]];
                }
            case 3: // 跨屏连续流
                return [_windowManager.SpanOverlay ?? primary ?? overlays[0]];
            default: // 仅主屏
                return [primary ?? overlays[0]];
        }
    }

    #region IOverlayWatchdogHost 显式实现（供看门狗做故障隔离与自动恢复）

    bool IOverlayWatchdogHost.Running
    {
        get => _runningFlag != 0;
        set => Interlocked.Exchange(ref _runningFlag, value ? 1 : 0);
    }

    IntPtr IOverlayWatchdogHost.ControlHandle => _controlWindow.Handle;

    IReadOnlyList<ScreenOverlay> IOverlayWatchdogHost.Overlays => _windowManager.Overlays;

    Thread? IOverlayWatchdogHost.RenderThread
    {
        get => _renderThread;
        set => _renderThread = value;
    }

    void IOverlayWatchdogHost.RequestRecoveryCleanup()
        // 仅置位标志：实际清理由新渲染线程在 RenderLoop 启动时执行，确保窗口操作线程亲和
        => _recoveryCleanupPending = true;

    /// <summary>
    /// 恢复清理的具体执行（仅由渲染线程调用）：销毁控制窗口与覆盖层窗口。
    /// 窗口句柄/DC 均由渲染线程创建，须在渲染线程执行 Win32.DestroyWindow 与
    /// OverlayWindowManager.Cleanup，且清理成功（句柄归零）后再由重建流程更新状态。
    /// </summary>
    private void PerformRecoveryCleanup()
    {
        _controlWindow.Destroy();
        CleanupOverlays();
    }

    void IOverlayWatchdogHost.Restart() => Start();

    #endregion

    public void Dispose()
    {
        Stop();
        DisposeLogiBatteryCache();
        DisposeHeartGeometry();
        _wicFactory.Dispose();
        _dwFactory.Dispose();
        _d2dFactory.Dispose();
    }
}
