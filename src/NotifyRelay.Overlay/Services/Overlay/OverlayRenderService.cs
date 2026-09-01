using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using NotifyRelay.Models.Render;
using Vortice;
using Vortice.Direct2D1;
using Vortice.DirectWrite;
using Vortice.Mathematics;
using Vortice.WIC;

namespace NotifyRelay.Services.Overlay;

public sealed partial class OverlayRenderService : IDisposable
{
    private readonly ILogger<OverlayRenderService> _logger;
    private readonly IOverlaySettings _settings;
    private readonly ID2D1Factory _d2dFactory;
    private readonly IDWriteFactory _dwFactory;
    private readonly IWICImagingFactory _wicFactory;

    private Thread? _renderThread;
    private volatile bool _running;
    private WndProcDelegate? _wndProcDelegate;
    private bool _classRegistered;

    // 渲染线程心跳 + 看门狗（故障隔离）：渲染循环每帧更新心跳，
    // 看门狗线程检测心跳超时后清除置顶并自动恢复，避免覆盖层卡死拖死整个应用。
    private long _lastHeartbeatTick;
    private Thread? _watchdogThread;
    private const int WatchdogIntervalMs = 2000;     // 看门狗轮询间隔
    private const int HeartbeatTimeoutMs = 10000;    // 心跳超时阈值（判定卡死）
    private const int WindowProbeTimeoutMs = 800;    // 窗口响应探测超时
    private int _consecutiveRenderFails;
    private const int MaxConsecutiveRenderFails = 60;

    // 每屏一个覆盖层窗口；顶部媒体/SuperIsland 卡片仅渲染在主屏覆盖层。
    private readonly List<ScreenOverlay> _overlays = [];
    private ScreenOverlay? _primaryOverlay;
    private ScreenOverlay? _spanOverlay;
    private string _overlaySignature = string.Empty;
    private volatile bool _displayDirty;

    // 顶部卡片（媒体 + SuperIsland），由 _lock 保护
    private readonly List<OverlayItem> _topItems = [];
    private readonly object _lock = new();

    // 弹幕请求队列（跨线程），由渲染线程分发到各屏覆盖层
    private readonly ConcurrentQueue<DanmakuRequest> _requests = new();

    // 延迟释放队列：D2D/DWrite 对象只在渲染线程释放；
    // 业务线程（媒体/BLE 推送等）一律入队，由渲染线程每帧开头统一释放，
    // 避免绘制期间并发释放导致本机崩溃（ExecutionEngineException）。
    private readonly ConcurrentQueue<IDisposable> _released = new();

    private DanmakuStyleSettings _currentStyle = new();
    private volatile int _maxFps;         // 0 = 跟随刷新率(DwmFlush)；否则为帧率上限
    private int _screenMode;              // 当前多屏模式，变化时触发覆盖层重建
    private readonly Random _rand = new();

    // 快捷键映射触发时的提示文本（由钩子线程推送，渲染线程消费，受 _lock 保护）
    private string? _keyMappingHintText;
    private long _keyMappingHintTick;     // Stopwatch 时间戳
    private const int KeyMappingHintTimeoutMs = 1500;

    public OverlayRenderService(ILogger<OverlayRenderService> logger, IOverlaySettings settings)
    {
        _logger = logger;
        _settings = settings;
        _d2dFactory = D2D1.D2D1CreateFactory<ID2D1Factory>();
        _dwFactory = DWrite.DWriteCreateFactory<IDWriteFactory>();
        _wicFactory = new IWICImagingFactory();
    }

    /// <summary>单个屏幕（或跨屏）的覆盖层窗口及其资源。</summary>
    private sealed class ScreenOverlay
    {
        public IntPtr Hwnd;
        public IntPtr MemDC;
        public IntPtr HBitmap;
        public IntPtr OldBitmap;
        public ID2D1DCRenderTarget? RenderTarget;
        public int OriginX;
        public int OriginY;
        public int Width;
        public int Height;
        public bool IsPrimary;
        public bool IsSpan;
        public bool Visible;
        public string DeviceName = string.Empty;
        public float TopOffset;   // 主屏顶部卡片占用高度，用于弹幕轨道起点
        public readonly List<DanmakuItem> Items = [];
        public readonly Queue<DanmakuItem> Pending = new();
    }

    private struct ScreenInfo
    {
        public string DeviceName;
        public int X, Y, W, H;
        public bool IsPrimary;
    }

    private sealed class DanmakuRequest
    {
        public string Text = string.Empty;
        public byte[]? IconPng;
        public DanmakuStyleSettings Settings = new();
    }

    public void Start()
    {
        if (_running) return;
        _running = true;
        // 启动时从已保存设置初始化样式，避免首条弹幕使用默认值（需手动调节才生效）
        LoadInitialStyle();
        LoadInitialHeartRateConfig();
        Interlocked.Exchange(ref _lastHeartbeatTick, Stopwatch.GetTimestamp());
        _renderThread = new Thread(RenderLoop) { IsBackground = true, Name = "D2D-Overlay" };
        _renderThread.SetApartmentState(ApartmentState.STA);
        _renderThread.Start();
        if (_watchdogThread == null || !_watchdogThread.IsAlive)
        {
            _watchdogThread = new Thread(WatchdogLoop) { IsBackground = true, Name = "D2D-Overlay-Watchdog" };
            _watchdogThread.Start();
        }
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
        _running = false;
        _renderThread?.Join(2000);
        CleanupOverlays();
    }

    private void RenderLoop()
    {
        timeBeginPeriod(1);
        try
        {
            EnsureWindowClass();
            InstallTopmostMonitor();
            SyncOverlays();
            _ctrlHwnd = CreateControlWindow();

            var timer = Stopwatch.StartNew();
            double nextFrame = 0;
            int frameCount = 0;
            bool anyVisible = false;

            while (_running)
            {
                while (PeekMessageW(out var msg, IntPtr.Zero, 0, 0, 1))
                {
                    TranslateMessage(ref msg);
                    DispatchMessageW(ref msg);
                }

                // 渲染线程心跳：供看门狗检测卡死（消息泵或渲染卡住都会停止更新）
                Interlocked.Exchange(ref _lastHeartbeatTick, Stopwatch.GetTimestamp());

                // 释放业务线程延迟入队的 D2D/DWrite 对象（锁外，绘制前统一执行）
                FlushReleasedResources();

                // 定期检测显示器热插拔 / 模式变更
                if ((frameCount++ & 63) == 0 || _displayDirty)
                {
                    _displayDirty = false;
                    SyncOverlays();
                    // 周期巡检兜底：无条件重断言 TOPMOST 层级（约每秒一次，成本极低）。
                    // 覆盖"标志仍在但 z-order 被全屏游戏/系统重建压到普通窗口之下"的失效场景，
                    // 以及 WinEvent 钩子漏触发的置顶重置。
                    ReassertOverlaysTopmost();
                }

                DispatchRequests();

                bool hasContent = TopItemsActive();
                if (!hasContent)
                {
                    foreach (var o in _overlays)
                        if (o.Items.Count > 0 || o.Pending.Count > 0) { hasContent = true; break; }
                }
                if (!hasContent && !_requests.IsEmpty) hasContent = true;
                if (!hasContent) hasContent = HeartRateActive();
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
                        foreach (var o in _overlays)
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
                    foreach (var o in _overlays)
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
            UninstallHookAndControlWindow();
            UninstallTopmostMonitor();
            timeEndPeriod(1);
            // 渲染线程退出时销毁自身创建的窗口，避免全屏覆盖层滞留屏幕抢占鼠标/层级
            CleanupOverlays();
        }
    }

    /// <summary>看门狗线程：周期性检查渲染线程心跳，超时则执行故障隔离与自动恢复。</summary>
    private void WatchdogLoop()
    {
        while (true)
        {
            Thread.Sleep(WatchdogIntervalMs);
            if (!_running) continue;   // 正常停止中，跳过
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

        _running = false;

        // 探测渲染线程消息泵是否仍响应（SMTO_ABORTIFHUNG 超时立即返回，不阻塞看门狗线程）
        if (_ctrlHwnd != IntPtr.Zero)
            SendMessageTimeoutW(_ctrlHwnd, WM_NULL, IntPtr.Zero, IntPtr.Zero,
                SMTO_ABORTIFHUNG, WindowProbeTimeoutMs, out _);

        // 兜底清除置顶：SetWindowLongPtr 不依赖窗口消息泵，即使窗口线程卡死也立即生效
        foreach (var o in _overlays)
        {
            if (o.Hwnd == IntPtr.Zero || !IsWindow(o.Hwnd)) continue;
            long exStyle = GetWindowLongPtr(o.Hwnd, GWL_EXSTYLE).ToInt64();
            if ((exStyle & WS_EX_TOPMOST) != 0)
                SetWindowLongPtr(o.Hwnd, GWL_EXSTYLE, new IntPtr(exStyle & ~WS_EX_TOPMOST));
        }

        // 等待旧渲染线程退出（卡死的线程恢复后会在 finally 中自毁窗口）
        var dead = _renderThread;
        _renderThread = null;
        bool exited = dead == null;
        for (int i = 0; i < 20 && !exited; i++)
        {
            dead!.Join(500);
            exited = !dead.IsAlive;
        }

        if (exited)
        {
            // 旧线程可能未执行 finally：清理控制窗口与覆盖层窗口，由重启后的新线程重建
            if (_ctrlHwnd != IntPtr.Zero)
            {
                DestroyWindow(_ctrlHwnd);
                _ctrlHwnd = IntPtr.Zero;
            }
            CleanupOverlays();
            Start();
            _logger.LogWarning("覆盖层渲染线程已恢复并重启");
        }
        else
        {
            _logger.LogError("覆盖层渲染线程永久卡死无法退出，已隔离（清除置顶），覆盖层停止工作，请重启应用恢复");
        }
    }

    private void EnsureWindowClass()
    {
        if (_classRegistered) return;
        _wndProcDelegate = WndProc;
        var hInstance = GetModuleHandleW(null);
        var wc = new WNDCLASSW
        {
            lpfnWndProc = Marshal.GetFunctionPointerForDelegate(_wndProcDelegate),
            hInstance = hInstance,
            lpszClassName = "NotifyRelayD2DOverlay"
        };
        RegisterClassW(ref wc);
        _classRegistered = true;
    }

    private ScreenOverlay CreateOverlayWindow(int x, int y, int width, int height)
    {
        var hInstance = GetModuleHandleW(null);
        uint exStyle = WS_EX_LAYERED | WS_EX_TRANSPARENT | WS_EX_TOPMOST
                     | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE;

        var hwnd = CreateWindowExW(
            exStyle, "NotifyRelayD2DOverlay", "NotifyRelayOverlay", WS_POPUP,
            x, y, width, height,
            IntPtr.Zero, IntPtr.Zero, hInstance, IntPtr.Zero);

        SetWindowPos(hwnd, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);

        var screenDC = GetDC(IntPtr.Zero);
        var memDC = CreateCompatibleDC(screenDC);

        var bmi = new BITMAPINFO();
        bmi.bmiHeader.biSize = Marshal.SizeOf<BITMAPINFOHEADER>();
        bmi.bmiHeader.biWidth = width;
        bmi.bmiHeader.biHeight = -height;
        bmi.bmiHeader.biPlanes = 1;
        bmi.bmiHeader.biBitCount = 32;
        bmi.bmiHeader.biCompression = BI_RGB;
        var hBitmap = CreateDIBSection(screenDC, ref bmi, DIB_RGB_COLORS, out _, IntPtr.Zero, 0);
        var oldBitmap = SelectObject(memDC, hBitmap);
        ReleaseDC(IntPtr.Zero, screenDC);

        var props = new RenderTargetProperties
        {
            Type = RenderTargetType.Default,
            PixelFormat = new Vortice.DCommon.PixelFormat(
                Vortice.DXGI.Format.B8G8R8A8_UNorm,
                Vortice.DCommon.AlphaMode.Premultiplied),
            DpiX = 96,
            DpiY = 96
        };
        var rt = _d2dFactory.CreateDCRenderTarget(props);
        rt.BindDC(memDC, new RawRect(0, 0, width, height));

        return new ScreenOverlay
        {
            Hwnd = hwnd,
            MemDC = memDC,
            HBitmap = hBitmap,
            OldBitmap = oldBitmap,
            RenderTarget = rt,
            OriginX = x,
            OriginY = y,
            Width = width,
            Height = height
        };
    }

    /// <summary>
    /// 重新断言所有覆盖层窗口的 TOPMOST z-order。
    /// 显示模式切换（如独占全屏游戏退出）会导致系统重建窗口 z-order、
    /// 丢失创建时设置的顶层状态，需在切换后/重新显示时主动重设。
    /// NOMOVE|NOSIZE|NOACTIVATE 无移动/缩放/激活副作用，不会引起闪烁。
    /// </summary>
    private void ReassertTopmost()
    {
        foreach (var o in _overlays)
            if (o.Hwnd != IntPtr.Zero)
                SetWindowPos(o.Hwnd, HWND_TOPMOST, 0, 0, 0, 0,
                    SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
    }

    /// <summary>枚举所有显示器（使用完整屏幕区域，弹幕可跨越整屏宽度）。</summary>
    private List<ScreenInfo> EnumerateScreens()
    {
        var list = new List<ScreenInfo>();
        EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, (IntPtr hMon, IntPtr hdc, ref RECT rc, IntPtr data) =>
        {
            var mi = new MONITORINFOEX { cbSize = Marshal.SizeOf<MONITORINFOEX>() };
            if (GetMonitorInfoW(hMon, ref mi))
            {
                list.Add(new ScreenInfo
                {
                    DeviceName = mi.szDevice ?? string.Empty,
                    X = mi.rcMonitor.Left,
                    Y = mi.rcMonitor.Top,
                    W = mi.rcMonitor.Right - mi.rcMonitor.Left,
                    H = mi.rcMonitor.Bottom - mi.rcMonitor.Top,
                    IsPrimary = (mi.dwFlags & MONITORINFOF_PRIMARY) != 0
                });
            }
            return true;
        }, IntPtr.Zero);

        if (list.Count == 0)
        {
            // 回退到主屏
            list.Add(new ScreenInfo
            {
                DeviceName = "PRIMARY",
                X = 0,
                Y = 0,
                W = GetSystemMetrics(SM_CXSCREEN),
                H = GetSystemMetrics(SM_CYSCREEN),
                IsPrimary = true
            });
        }
        return list;
    }

    /// <summary>根据当前多屏模式与显示器布局，按需重建覆盖层窗口集合。</summary>
    private void SyncOverlays()
    {
        int mode;
        lock (_lock) mode = _currentStyle.DisplayScreenMode;
        _screenMode = mode;

        var screens = EnumerateScreens();
        var sig = mode + "|" + string.Join(";", screens.ConvertAll(s => $"{s.X},{s.Y},{s.W},{s.H},{(s.IsPrimary ? 1 : 0)}"));
        if (sig == _overlaySignature && _overlays.Count > 0) return;
        _overlaySignature = sig;

        // 顶部卡片的设备资源绑定在旧的主屏 RenderTarget 上，重建前需失效
        InvalidateTopItemDeviceResources();

        foreach (var o in _overlays) DestroyOverlay(o);
        _overlays.Clear();
        _primaryOverlay = null;
        _spanOverlay = null;

        var primary = screens.Find(s => s.IsPrimary);
        if (primary.W == 0) primary = screens[0];

        // 模式 1(所有屏)/2(鼠标屏) 需要每屏一个窗口；模式 0(主屏)/3(跨屏) 仅需主屏窗口承载卡片
        List<ScreenInfo> targets = (mode == 1 || mode == 2) ? screens : [primary];
        foreach (var s in targets)
        {
            var o = CreateOverlayWindow(s.X, s.Y, s.W, s.H);
            o.DeviceName = s.DeviceName;
            o.IsPrimary = s.IsPrimary;
            _overlays.Add(o);
            if (s.IsPrimary) _primaryOverlay = o;
        }
        if (_primaryOverlay == null && _overlays.Count > 0)
        {
            _overlays[0].IsPrimary = true;
            _primaryOverlay = _overlays[0];
        }

        if (mode == 3)
        {
            int minX = int.MaxValue, minY = int.MaxValue, maxX = int.MinValue, maxY = int.MinValue;
            foreach (var s in screens)
            {
                minX = Math.Min(minX, s.X);
                minY = Math.Min(minY, s.Y);
                maxX = Math.Max(maxX, s.X + s.W);
                maxY = Math.Max(maxY, s.Y + s.H);
            }
            var span = CreateOverlayWindow(minX, minY, maxX - minX, maxY - minY);
            span.IsSpan = true;
            _spanOverlay = span;
            _overlays.Add(span);
        }
    }

    private void DestroyOverlay(ScreenOverlay o)
    {
        foreach (var item in o.Items) item.Dispose();
        o.Items.Clear();
        foreach (var item in o.Pending) item.Dispose();
        o.Pending.Clear();

        o.RenderTarget?.Dispose();
        o.RenderTarget = null;

        if (o.MemDC != IntPtr.Zero)
        {
            SelectObject(o.MemDC, o.OldBitmap);
            DeleteObject(o.HBitmap);
            DeleteDC(o.MemDC);
            o.MemDC = IntPtr.Zero;
        }
        if (o.Hwnd != IntPtr.Zero)
        {
            DestroyWindow(o.Hwnd);
            o.Hwnd = IntPtr.Zero;
        }
    }

    private void CleanupOverlays()
    {
        foreach (var o in _overlays) DestroyOverlay(o);
        _overlays.Clear();
        _primaryOverlay = null;
        _spanOverlay = null;

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
    {
        if (d != null) _released.Enqueue(d);
    }

    /// <summary>渲染线程调用：释放业务线程入队的 D2D/DWrite 对象（仅在渲染线程执行释放）。</summary>
    private void FlushReleasedResources()
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
        bool hasLogiBatteryContent = IsLogiBatteryTarget(o) && HasLogiBatteryContent();
        bool hasContent = o.Items.Count > 0 || o.Pending.Count > 0
            || (o.IsPrimary && TopItemsActive())
            || isHeartRateTarget
            || hasKeyboardContent
            || hasLogiBatteryContent;
        if (!hasContent)
        {
            if (o.Visible) { ShowWindow(o.Hwnd, SW_HIDE); o.Visible = false; }
            return;
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
            ScheduleReassertTopmost();
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
        if (_overlays.Count == 0) return [];
        switch (mode)
        {
            case 1: // 所有屏幕
                return _overlays.FindAll(o => !o.IsSpan);
            case 2: // 鼠标所在屏幕
                GetCursorPos(out var pt);
                var hit = _overlays.Find(o => !o.IsSpan
                    && pt.X >= o.OriginX && pt.X < o.OriginX + o.Width
                    && pt.Y >= o.OriginY && pt.Y < o.OriginY + o.Height);
                return [hit ?? _primaryOverlay ?? _overlays[0]];
            case 3: // 跨屏连续流
                return [_spanOverlay ?? _primaryOverlay ?? _overlays[0]];
            default: // 仅主屏
                return [_primaryOverlay ?? _overlays[0]];
        }
    }

    public void Dispose()
    {
        Stop();
        DisposeHeartGeometry();
        _wicFactory.Dispose();
        _dwFactory.Dispose();
        _d2dFactory.Dispose();
    }
}
