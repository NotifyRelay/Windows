using System.Runtime.InteropServices;
using Vortice;
using Vortice.Direct2D1;

namespace NotifyRelay.Services.Overlay;

/// <summary>
/// 覆盖层窗口与渲染核心（窗口句柄 / 内存 DC / DIB 位图 / D2D DC 渲染目标）的创建、销毁与多屏同步。
/// 全部成员仅由渲染线程调用，调用方需自行保证线程约束。
/// </summary>
internal sealed class OverlayWindowManager
{
    private readonly ID2D1Factory _d2dFactory;
    private readonly ILogger _logger;

    private readonly List<ScreenOverlay> _overlays = [];
    private string _overlaySignature = string.Empty;
    private ScreenOverlay? _primaryOverlay;
    private ScreenOverlay? _spanOverlay;

    public OverlayWindowManager(ID2D1Factory d2dFactory, ILogger logger)
    {
        _d2dFactory = d2dFactory;
        _logger = logger;
    }

    /// <summary>当前所有覆盖层窗口（每屏一个，跨屏模式下额外一个跨屏窗口）。仅渲染线程读写。</summary>
    public IReadOnlyList<ScreenOverlay> Overlays => _overlays;

    public ScreenOverlay? PrimaryOverlay => _primaryOverlay;

    public ScreenOverlay? SpanOverlay => _spanOverlay;

    /// <summary>创建单个分层覆盖层窗口及其 D2D 渲染目标。</summary>
    public ScreenOverlay CreateOverlayWindow(int x, int y, int width, int height)
    {
        var hInstance = Win32.GetModuleHandleW(null);
        uint exStyle = Win32.WS_EX_LAYERED | Win32.WS_EX_TRANSPARENT | Win32.WS_EX_TOPMOST
                     | Win32.WS_EX_TOOLWINDOW | Win32.WS_EX_NOACTIVATE;

        var hwnd = Win32.CreateWindowExW(
            exStyle, Win32.OverlayWindowClass, "NotifyRelayOverlay", Win32.WS_POPUP,
            x, y, width, height,
            IntPtr.Zero, IntPtr.Zero, hInstance, IntPtr.Zero);

        Win32.SetWindowPos(hwnd, Win32.HWND_TOPMOST, 0, 0, 0, 0,
            Win32.SWP_NOMOVE | Win32.SWP_NOSIZE | Win32.SWP_NOACTIVATE);

        var screenDC = Win32.GetDC(IntPtr.Zero);
        var memDC = Win32.CreateCompatibleDC(screenDC);

        var bmi = new Win32.BITMAPINFO();
        bmi.bmiHeader.biSize = Marshal.SizeOf<Win32.BITMAPINFOHEADER>();
        bmi.bmiHeader.biWidth = width;
        bmi.bmiHeader.biHeight = -height;
        bmi.bmiHeader.biPlanes = 1;
        bmi.bmiHeader.biBitCount = 32;
        bmi.bmiHeader.biCompression = Win32.BI_RGB;
        var hBitmap = Win32.CreateDIBSection(screenDC, ref bmi, Win32.DIB_RGB_COLORS, out _, IntPtr.Zero, 0);
        var oldBitmap = Win32.SelectObject(memDC, hBitmap);
        Win32.ReleaseDC(IntPtr.Zero, screenDC);

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

    /// <summary>销毁单个覆盖层窗口及其全部 D2D/GDI 资源。</summary>
    public void DestroyOverlay(ScreenOverlay o)
    {
        foreach (var item in o.Items) item.Dispose();
        o.Items.Clear();
        foreach (var item in o.Pending) item.Dispose();
        o.Pending.Clear();

        o.RenderTarget?.Dispose();
        o.RenderTarget = null;

        if (o.MemDC != IntPtr.Zero)
        {
            Win32.SelectObject(o.MemDC, o.OldBitmap);
            Win32.DeleteObject(o.HBitmap);
            Win32.DeleteDC(o.MemDC);
            o.MemDC = IntPtr.Zero;
        }
        if (o.Hwnd != IntPtr.Zero)
        {
            Win32.DestroyWindow(o.Hwnd);
            o.Hwnd = IntPtr.Zero;
        }
    }

    /// <summary>销毁并清空所有覆盖层窗口（不处理顶部卡片等业务数据）。</summary>
    public void Cleanup()
    {
        foreach (var o in _overlays) DestroyOverlay(o);
        _overlays.Clear();
        _primaryOverlay = null;
        _spanOverlay = null;
    }

    /// <summary>枚举所有显示器（使用完整屏幕区域，弹幕可跨越整屏宽度）。</summary>
    public static List<ScreenInfo> EnumerateScreens()
    {
        var list = new List<ScreenInfo>();
        Win32.EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, (IntPtr hMon, IntPtr hdc, ref Win32.RECT rc, IntPtr data) =>
        {
            var mi = new Win32.MONITORINFOEX { cbSize = Marshal.SizeOf<Win32.MONITORINFOEX>() };
            if (Win32.GetMonitorInfoW(hMon, ref mi))
            {
                list.Add(new ScreenInfo
                {
                    DeviceName = mi.szDevice ?? string.Empty,
                    X = mi.rcMonitor.Left,
                    Y = mi.rcMonitor.Top,
                    W = mi.rcMonitor.Right - mi.rcMonitor.Left,
                    H = mi.rcMonitor.Bottom - mi.rcMonitor.Top,
                    IsPrimary = (mi.dwFlags & Win32.MONITORINFOF_PRIMARY) != 0
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
                W = Win32.GetSystemMetrics(Win32.SM_CXSCREEN),
                H = Win32.GetSystemMetrics(Win32.SM_CYSCREEN),
                IsPrimary = true
            });
        }
        return list;
    }

    /// <summary>
    /// 根据当前多屏模式与显示器布局，按需重建覆盖层窗口集合。
    /// 签名（模式 + 各屏几何）未变化时直接返回 false，不产生任何窗口操作。
    /// </summary>
    /// <param name="mode">多屏模式：0 仅主屏 / 1 所有屏 / 2 鼠标所在屏 / 3 跨屏连续流。</param>
    /// <param name="onBeforeRebuild">重建前回调（用于使绑定在旧渲染目标上的业务资源失效）。</param>
    /// <returns>是否发生了重建。</returns>
    public bool SyncOverlays(int mode, Action? onBeforeRebuild = null)
    {
        var screens = EnumerateScreens();
        var sig = mode + "|" + string.Join(";", screens.ConvertAll(s => $"{s.X},{s.Y},{s.W},{s.H},{(s.IsPrimary ? 1 : 0)}"));
        if (sig == _overlaySignature && _overlays.Count > 0) return false;
        _overlaySignature = sig;

        // 顶部卡片的设备资源绑定在旧的主屏 RenderTarget 上，重建前需失效
        onBeforeRebuild?.Invoke();

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

        if (_overlays.Count == 0)
            _logger.LogWarning("覆盖层窗口同步完成但窗口数量为 0（模式 {Mode}，显示器 {Count} 块）", mode, screens.Count);

        return true;
    }
}
