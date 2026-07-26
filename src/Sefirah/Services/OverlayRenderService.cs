using System.Runtime.InteropServices;
using Vortice;
using Vortice.Direct2D1;
using Vortice.DirectWrite;
using Vortice.Mathematics;
using Vortice.WIC;
using NotifyRelay.Models.Render;
using NotifyRelay.Data.Contracts;

namespace NotifyRelay.Services;

public sealed partial class OverlayRenderService : IDisposable
{
    private readonly ILogger<OverlayRenderService> _logger;
    private readonly IGeneralSettingsService _settings;
    private readonly ID2D1Factory _d2dFactory;
    private readonly IDWriteFactory _dwFactory;
    private readonly IWICImagingFactory _wicFactory;

    private Thread? _renderThread;
    private volatile bool _running;
    private WndProcDelegate? _wndProcDelegate;
    private bool _classRegistered;

    private IntPtr _hwnd;
    private IntPtr _memDC;
    private IntPtr _hBitmap;
    private IntPtr _oldBitmap;
    private ID2D1DCRenderTarget? _renderTarget;
    private int _width;
    private int _height;

    private readonly List<OverlayItem> _items = [];
    private readonly object _lock = new();

    private DanmakuStyleSettings _currentStyle = new();

    public OverlayRenderService(ILogger<OverlayRenderService> logger, IGeneralSettingsService settings)
    {
        _logger = logger;
        _settings = settings;
        _d2dFactory = D2D1.D2D1CreateFactory<ID2D1Factory>();
        _dwFactory = DWrite.DWriteCreateFactory<IDWriteFactory>();
        _wicFactory = new IWICImagingFactory();
    }

    public void Start()
    {
        if (_running) return;
        _running = true;
        _renderThread = new Thread(RenderLoop) { IsBackground = true, Name = "D2D-Overlay" };
        _renderThread.SetApartmentState(ApartmentState.STA);
        _renderThread.Start();
    }

    public void Stop()
    {
        _running = false;
        _renderThread?.Join(2000);
        CleanupOverlay();
    }

    private void RenderLoop()
    {
        CreateOverlay();

        var timer = Stopwatch.StartNew();
        const double targetFrameTime = 1.0 / 120.0;
        double nextFrameTime = timer.Elapsed.TotalSeconds;

        while (_running)
        {
            while (PeekMessageW(out var msg, IntPtr.Zero, 0, 0, 1))
            {
                TranslateMessage(ref msg);
                DispatchMessageW(ref msg);
            }

            // 精确等待至下一帧起始
            double now = timer.Elapsed.TotalSeconds;
            if (now < nextFrameTime)
            {
                double remain = nextFrameTime - now;
                int waitMs = (int)(remain * 1000);
                if (waitMs > 1)
                    Thread.Sleep(waitMs - 1);
                while (timer.Elapsed.TotalSeconds < nextFrameTime)
                    Thread.SpinWait(10);
            }
            nextFrameTime = timer.Elapsed.TotalSeconds + targetFrameTime;

            lock (_lock)
            {
                RenderFrame();
            }

            DwmFlush();
        }
    }

    private void CreateOverlay()
    {
        _wndProcDelegate = WndProc;
        var hInstance = GetModuleHandleW(null);
        string className = "NotifyRelayD2DOverlay";

        if (!_classRegistered)
        {
            var wc = new WNDCLASSW
            {
                lpfnWndProc = Marshal.GetFunctionPointerForDelegate(_wndProcDelegate),
                hInstance = hInstance,
                lpszClassName = className
            };
            RegisterClassW(ref wc);
            _classRegistered = true;
        }

        uint exStyle = WS_EX_LAYERED | WS_EX_TRANSPARENT | WS_EX_TOPMOST
                     | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE;

        _width = GetSystemMetrics(SM_CXSCREEN);
        _height = GetSystemMetrics(SM_CYSCREEN);

        _hwnd = CreateWindowExW(
            exStyle, className, "NotifyRelayOverlay", WS_POPUP,
            0, 0, _width, _height,
            IntPtr.Zero, IntPtr.Zero, hInstance, IntPtr.Zero);

        ShowWindow(_hwnd, SW_SHOWNOACTIVATE);
        SetWindowPos(_hwnd, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);

        var screenDC = GetDC(IntPtr.Zero);
        _memDC = CreateCompatibleDC(screenDC);

        var bmi = new BITMAPINFO();
        bmi.bmiHeader.biSize = Marshal.SizeOf<BITMAPINFOHEADER>();
        bmi.bmiHeader.biWidth = _width;
        bmi.bmiHeader.biHeight = -_height;
        bmi.bmiHeader.biPlanes = 1;
        bmi.bmiHeader.biBitCount = 32;
        bmi.bmiHeader.biCompression = BI_RGB;
        _hBitmap = CreateDIBSection(screenDC, ref bmi, DIB_RGB_COLORS, out _, IntPtr.Zero, 0);
        _oldBitmap = SelectObject(_memDC, _hBitmap);
        ReleaseDC(IntPtr.Zero, screenDC);

        var props = new RenderTargetProperties
        {
            Type = RenderTargetType.Default,
            PixelFormat = new Vortice.DCommon.PixelFormat(
                Vortice.DXGI.Format.B8G8R8A8_UNorm,
                Vortice.DCommon.AlphaMode.Premultiplied),
            DpiX = 96, DpiY = 96
        };

        _renderTarget = _d2dFactory.CreateDCRenderTarget(props);
        _renderTarget.BindDC(_memDC, new RawRect(0, 0, _width, _height));
    }

    private void CleanupOverlay()
    {
        lock (_lock)
        {
            foreach (var item in _items) item.Dispose();
            _items.Clear();
        }

        _renderTarget?.Dispose();
        _renderTarget = null;

        if (_memDC != IntPtr.Zero)
        {
            SelectObject(_memDC, _oldBitmap);
            DeleteObject(_hBitmap);
            DeleteDC(_memDC);
            _memDC = IntPtr.Zero;
        }

        if (_hwnd != IntPtr.Zero)
        {
            DestroyWindow(_hwnd);
            _hwnd = IntPtr.Zero;
        }
    }

    private void RenderFrame()
    {
        if (_renderTarget == null) return;

        _renderTarget.BeginDraw();
        _renderTarget.Clear(new Color4(0, 0, 0, 0));

        double now = Stopwatch.GetTimestamp();
        double freq = Stopwatch.Frequency;

        // Render MediaCards and SuperIslands at top area (Y: 0-260)
        RenderTopCards(now, freq);
        // Render danmaku items in the lower area
        RenderDanmakuItems(now, freq);

        _renderTarget.EndDraw();

        var ptSrc = new POINT(0, 0);
        var size = new SIZE(_width, _height);
        var ptDst = new POINT(0, 0);
        var blend = new BLENDFUNCTION { BlendOp = AC_SRC_OVER, BlendFlags = 0, SourceConstantAlpha = 255, AlphaFormat = AC_SRC_ALPHA };
        UpdateLayeredWindow(_hwnd, IntPtr.Zero, ref ptDst, ref size, _memDC, ref ptSrc, 0, ref blend, ULW_ALPHA);
    }

    public void Dispose()
    {
        Stop();
        _wicFactory.Dispose();
        _dwFactory.Dispose();
        _d2dFactory.Dispose();
    }
}
