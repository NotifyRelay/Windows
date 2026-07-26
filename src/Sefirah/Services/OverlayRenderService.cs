using System.Diagnostics;
using System.Drawing;
using System.Numerics;
using System.Runtime.InteropServices;
using NotifyRelay.Models.Render;
using NotifyRelay.Data.Contracts;
using Vortice;
using Vortice.Direct2D1;
using Vortice.DirectWrite;
using Vortice.Mathematics;
using Vortice.WIC;
using DWriteFontWeight = Vortice.DirectWrite.FontWeight;
using DWriteFontStyle = Vortice.DirectWrite.FontStyle;
using DWriteFontStretch = Vortice.DirectWrite.FontStretch;
using BitmapInterpolationMode = Vortice.Direct2D1.BitmapInterpolationMode;

namespace NotifyRelay.Services;

public sealed class OverlayRenderService : IDisposable
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

    public void ShowDanmaku(string appName, string title, string body, byte[]? iconPng, string deviceName)
    {
        var text = string.IsNullOrEmpty(appName) ? $"{title} - {body}" : $"{appName}: {title} - {body}";

        lock (_lock)
        {
            var item = new DanmakuItem
            {
                Text = text,
                IconPng = iconPng,
                Settings = _currentStyle,
                StartTime = Stopwatch.GetTimestamp(),
                AppName = appName,
                DeviceName = deviceName
            };
            AssignTrack(item);
            _items.Add(item);
        }
    }

    public void ShowMediaCard(string deviceId, string deviceName, string title, string artist, byte[]? coverPng, bool isPlaying)
    {
        lock (_lock)
        {
            var existing = _items.OfType<MediaCardItem>().FirstOrDefault(m => m.DeviceId == deviceId);
            if (existing != null)
            {
                // 空值表示"未改变"，仅更新有实际值的字段
                bool titleChanged = false;
                bool artistChanged = false;
                if (!string.IsNullOrEmpty(title) && title != existing.Title)
                {
                    existing.Title = title;
                    existing.TitleLayout?.Dispose();
                    existing.TitleLayout = null;
                    titleChanged = true;
                }
                if (!string.IsNullOrEmpty(artist) && artist != existing.Artist)
                {
                    existing.Artist = artist;
                    existing.ArtistLayout?.Dispose();
                    existing.ArtistLayout = null;
                    artistChanged = true;
                }
                if (coverPng != null)
                {
                    existing.CoverPng = coverPng;
                    existing.CoverBitmap?.Dispose();
                    existing.CoverBitmap = null;
                }
                existing.IsPlaying = isPlaying;
                existing.LastUpdateTime = Stopwatch.GetTimestamp();

                // title 且 artist 都变更时才触发展开（新曲目切换）
                if (titleChanged && artistChanged)
                {
                    existing.IsExpanded = true;
                    existing.ExpandedSince = Stopwatch.GetTimestamp();
                }
                return;
            }

            var now = Stopwatch.GetTimestamp();
            var item = new MediaCardItem
            {
                DeviceId = deviceId,
                DeviceName = deviceName,
                Title = title,
                Artist = artist,
                CoverPng = coverPng,
                IsPlaying = isPlaying,
                StartTime = now,
                LastUpdateTime = now,
                IsExpanded = true,
                ExpandedSince = now
            };
            _items.Add(item);
        }
    }

    public void RemoveMediaCard(string deviceId)
    {
        lock (_lock)
        {
            var item = _items.OfType<MediaCardItem>().FirstOrDefault(m => m.DeviceId == deviceId);
            if (item != null)
            {
                item.Active = false;
                item.Dispose();
                _items.Remove(item);
            }
        }
    }

    public void ShowSuperIsland(string sourceId, string deviceName, SuperIslandState state)
    {
        lock (_lock)
        {
            var existing = _items.OfType<SuperIslandItem>().FirstOrDefault(s => s.SourceId == sourceId);
            if (existing != null)
            {
                // 处理增量变更合并
                if (!string.IsNullOrEmpty(state.ChangesJson))
                {
                    existing.State.MergeChanges(state.ChangesJson);
                }

                // 空值表示"未改变"，仅合并有实际值的字段
                if (!string.IsNullOrEmpty(state.Title)) existing.State.Title = state.Title;
                if (!string.IsNullOrEmpty(state.Subtitle)) existing.State.Subtitle = state.Subtitle;
                if (!string.IsNullOrEmpty(state.Extra)) existing.State.Extra = state.Extra;
                if (state.IconPng != null) existing.State.IconPng = state.IconPng;
                if (state.Pics != null) existing.State.Pics = state.Pics;
                if (state.Progress > 0) existing.State.Progress = state.Progress;
                if (state.TimerType != TimerType.None) existing.State.TimerType = state.TimerType;
                if (state.TimerValue > 0) existing.State.TimerValue = state.TimerValue;
                if (state.TimerStartTime > 0) existing.State.TimerStartTime = state.TimerStartTime;
                if (!string.IsNullOrEmpty(state.ParamV2Raw))
                {
                    existing.State.ParamV2Raw = state.ParamV2Raw;
                    SuperIslandParamV2Parser.ApplyToState(existing.State, state.ParamV2Raw);
                }

                existing.LastUpdateTime = Stopwatch.GetTimestamp();

                // 触发 UI 刷新：使缓存的 Layout 失效
                existing.TitleLayout?.Dispose();
                existing.TitleLayout = null;
                existing.SubtitleLayout?.Dispose();
                existing.SubtitleLayout = null;
                existing.AdditionalTextLayout?.Dispose();
                existing.AdditionalTextLayout = null;
                existing.ExtraLayout?.Dispose();
                existing.ExtraLayout = null;

                // Extra 变更时重新展开
                if (!string.IsNullOrEmpty(state.Extra))
                {
                    existing.IsExpanded = true;
                    existing.ExpandedSince = Stopwatch.GetTimestamp();
                }
                return;
            }

            var item = new SuperIslandItem
            {
                SourceId = sourceId,
                DeviceName = deviceName,
                State = state,
                IconPng = state.IconPng,
                StartTime = Stopwatch.GetTimestamp(),
                LastUpdateTime = Stopwatch.GetTimestamp(),
                IsExpanded = true,
                ExpandedSince = Stopwatch.GetTimestamp()
            };
            _items.Add(item);
        }
    }

    public void RemoveSuperIsland(string sourceId)
    {
        lock (_lock)
        {
            var item = _items.OfType<SuperIslandItem>().FirstOrDefault(s => s.SourceId == sourceId);
            if (item != null)
            {
                item.Active = false;
                item.Dispose();
                _items.Remove(item);
            }
        }
    }

    public void UpdateStyle(DanmakuStyleSettings settings)
    {
        lock (_lock)
        {
            _currentStyle = settings;
        }
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

    private void RenderTopCards(double now, double freq)
    {
        float y = 10;
        var mediaItems = _items.OfType<MediaCardItem>().Where(m => m.Active).ToList();
        var superItems = _items.OfType<SuperIslandItem>().Where(s => s.Active).ToList();

        _logger.LogTrace("RenderTopCards: mediaCount={MediaCount}, superCount={SuperCount}, totalItems={TotalItems}",
            mediaItems.Count, superItems.Count, _items.Count);

        // Remove timed out items
        for (int i = mediaItems.Count - 1; i >= 0; i--)
        {
            var elapsed = (now - mediaItems[i].LastUpdateTime) / freq;
            if (elapsed > MediaCardItem.TimeoutSeconds)
            {
                _logger.LogTrace("RenderTopCards: 媒体卡片超时移除 deviceId={DeviceId}", mediaItems[i].DeviceId);
                mediaItems[i].Active = false;
                mediaItems[i].Dispose();
                _items.Remove(mediaItems[i]);
                mediaItems.RemoveAt(i);
            }
        }
        for (int i = superItems.Count - 1; i >= 0; i--)
        {
            var elapsed = (now - superItems[i].LastUpdateTime) / freq;
            if (elapsed > SuperIslandItem.TimeoutSeconds)
            {
                _logger.LogTrace("RenderTopCards: SuperIsland卡片超时移除 sourceId={SourceId}", superItems[i].SourceId);
                superItems[i].Active = false;
                superItems[i].Dispose();
                _items.Remove(superItems[i]);
                superItems.RemoveAt(i);
            }
        }

        // Render media card (max 1) — 居中灵动岛胶囊
        var media = mediaItems.FirstOrDefault();
        if (media != null)
        {
            // 自动收起：媒体字段变更后 5 秒自动收起
            if (media.IsExpanded && (now - media.ExpandedSince) / freq > MediaCardItem.AutoCollapseSeconds)
            {
                media.IsExpanded = false;
            }

            _logger.LogTrace("RenderTopCards: 渲染媒体卡片 deviceId={DeviceId}, title={Title}, expanded={Expanded}",
                media.DeviceId, media.Title, media.IsExpanded);
            EnsureMediaResources(media);
            DrawMediaCard(media, _renderTarget!, y);
            y += media.IsExpanded ? 108 : 48;
        }

        // Render SuperIsland cards (max 3) — 居中灵动岛胶囊
        foreach (var si in superItems.Take(3))
        {
            // 自动收起：Extra 信息展示后 5 秒自动收起为紧凑模式
            if (si.IsExpanded && si.State.HasExtra && (now - si.ExpandedSince) / freq > SuperIslandItem.AutoCollapseSeconds)
            {
                si.IsExpanded = false;
            }

            _logger.LogTrace("RenderTopCards: 渲染SuperIsland卡片 sourceId={SourceId}, title={Title}, expanded={Expanded}",
                si.SourceId, si.State.Title, si.IsExpanded);
            EnsureSuperIslandResources(si);
            DrawSuperIslandCard(si, _renderTarget!, y);
            y += si.IsExpanded ? 110 : 84;
        }
    }

    private void RenderDanmakuItems(double now, double freq)
    {
        for (int i = _items.Count - 1; i >= 0; i--)
        {
            if (_items[i] is DanmakuItem item)
            {
                if (!item.Active) continue;
                double elapsed = (now - item.StartTime) / freq;
                double x = item.SpawnX - elapsed * item.Settings.PixelsPerSecond;

                if (x < -item.TotalWidth - 50)
                {
                    item.Dispose();
                    _items.RemoveAt(i);
                    continue;
                }

                EnsureDanmakuResources(item);
                DrawDanmaku(item, (float)x, _renderTarget!);
            }
        }
    }

    private void EnsureDanmakuResources(DanmakuItem item)
    {
        if (item.TextLayout == null)
        {
            var s = _currentStyle;
            using var format = _dwFactory.CreateTextFormat(
                s.FontFamilyName, null,
                s.Bold ? DWriteFontWeight.Bold : DWriteFontWeight.Normal,
                DWriteFontStyle.Normal, DWriteFontStretch.Normal,
                (float)s.FontSize);

            item.TextLayout = _dwFactory.CreateTextLayout(
                item.Text, format, float.MaxValue, (float)s.FontSize * 2);

            var metrics = item.TextLayout.Metrics;
            item.TextWidth = metrics.Width;
            item.TextHeight = metrics.Height;
            item.TotalWidth = item.TextWidth
                + (item.IconPng != null ? (float)s.FontSize + 8 : 0) + 20;
        }

        if (item.IconBitmap == null && item.IconPng != null)
        {
            try { item.IconBitmap = LoadBitmapFromPng(item.IconPng, _renderTarget!); }
            catch
            {
                item.IconPng = null;
            }
        }
    }

    private void EnsureMediaResources(MediaCardItem item)
    {
        if (item.CoverBitmap == null && item.CoverPng != null)
        {
            try
            {
                _logger.LogDebug("EnsureMediaResources: 尝试加载封面图片, 数据长度={Length}", item.CoverPng.Length);
                item.CoverBitmap = LoadBitmapFromPng(item.CoverPng, _renderTarget!);
                _logger.LogDebug("EnsureMediaResources: 封面加载成功, 尺寸={Width}x{Height}",
                    item.CoverBitmap?.Size.Width, item.CoverBitmap?.Size.Height);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "EnsureMediaResources: 封面图片加载失败, 数据长度={Length}", item.CoverPng?.Length);
            }
        }

        if (item.TitleLayout == null && !string.IsNullOrEmpty(item.Title))
        {
            using var format = _dwFactory.CreateTextFormat(
                "Microsoft YaHei", null,
                DWriteFontWeight.Bold, DWriteFontStyle.Normal, DWriteFontStretch.Normal, 18);
            item.TitleLayout = _dwFactory.CreateTextLayout(item.Title, format, 400, 30);
        }

        if (item.ArtistLayout == null && !string.IsNullOrEmpty(item.Artist))
        {
            using var format = _dwFactory.CreateTextFormat(
                "Microsoft YaHei", null,
                DWriteFontWeight.Normal, DWriteFontStyle.Normal, DWriteFontStretch.Normal, 14);
            item.ArtistLayout = _dwFactory.CreateTextLayout(item.Artist, format, 400, 25);
        }
    }

    private void EnsureSuperIslandResources(SuperIslandItem item)
    {
        if (item.IconBitmap == null && item.IconPng != null)
        {
            try { item.IconBitmap = LoadBitmapFromPng(item.IconPng, _renderTarget!); }
            catch { }
        }

        if (item.TitleLayout == null && !string.IsNullOrEmpty(item.State.Title))
        {
            using var format = _dwFactory.CreateTextFormat(
                "Microsoft YaHei", null,
                DWriteFontWeight.Bold, DWriteFontStyle.Normal, DWriteFontStretch.Normal, 16);
            item.TitleLayout = _dwFactory.CreateTextLayout(item.State.Title, format, 400, 25);
        }

        if (item.SubtitleLayout == null && !string.IsNullOrEmpty(item.State.Subtitle))
        {
            using var format = _dwFactory.CreateTextFormat(
                "Microsoft YaHei", null,
                DWriteFontWeight.Normal, DWriteFontStyle.Normal, DWriteFontStretch.Normal, 13);
            item.SubtitleLayout = _dwFactory.CreateTextLayout(item.State.Subtitle, format, 400, 22);
        }

        if (item.AdditionalTextLayout == null && !string.IsNullOrEmpty(item.State.AdditionalText))
        {
            using var format = _dwFactory.CreateTextFormat(
                "Microsoft YaHei", null,
                DWriteFontWeight.Normal, DWriteFontStyle.Normal, DWriteFontStretch.Normal, 12);
            item.AdditionalTextLayout = _dwFactory.CreateTextLayout(item.State.AdditionalText, format, 400, 20);
        }

        if (item.ExtraLayout == null && !string.IsNullOrEmpty(item.State.Extra))
        {
            using var format = _dwFactory.CreateTextFormat(
                "Microsoft YaHei", null,
                DWriteFontWeight.Normal, DWriteFontStyle.Normal, DWriteFontStretch.Normal, 11);
            item.ExtraLayout = _dwFactory.CreateTextLayout(item.State.Extra, format, 380, 20);
        }
    }

    private void DrawDanmaku(DanmakuItem item, float x, ID2D1DCRenderTarget rt)
    {
        if (item.TextLayout == null) return;

        var s = _currentStyle;
        float y = item.TrackY;
        float opacity = s.Opacity;
        float iconOffset = 0;

        if (item.IconBitmap != null)
        {
            float iconSize = (float)s.FontSize;
            var destRect = new Vortice.Mathematics.Rect((int)(x + 10), (int)y, (int)iconSize, (int)iconSize);
            rt.DrawBitmap(item.IconBitmap, opacity, BitmapInterpolationMode.Linear, destRect);
            iconOffset = iconSize + 8;
        }

        float textX = x + 10 + iconOffset;
        float textY = y;

        if (s.ShadowEnabled)
        {
            float sd = (float)s.ShadowDepth;
            float so = s.ShadowOpacityFloat * opacity;
            using var shadowBrush = rt.CreateSolidColorBrush(
                new Color4(s.ShadowColorR / 255f, s.ShadowColorG / 255f,
                           s.ShadowColorB / 255f, so));
            rt.DrawTextLayout(new Vector2(textX + sd, textY + sd), item.TextLayout, shadowBrush);
        }

        if (s.BorderEnabled && s.BorderThickness > 0)
        {
            float bt = (float)s.BorderThickness;
            using var strokeBrush = rt.CreateSolidColorBrush(
                new Color4(s.BorderColorR / 255f, s.BorderColorG / 255f,
                           s.BorderColorB / 255f, opacity));
            for (int dx = -1; dx <= 1; dx++)
            for (int dy = -1; dy <= 1; dy++)
            {
                if (dx == 0 && dy == 0) continue;
                rt.DrawTextLayout(new Vector2(textX + dx * bt, textY + dy * bt),
                    item.TextLayout, strokeBrush);
            }
        }

        using var fillBrush = rt.CreateSolidColorBrush(
            new Color4(s.ColorR / 255f, s.ColorG / 255f,
                       s.ColorB / 255f, opacity));
        rt.DrawTextLayout(new Vector2(textX, textY), item.TextLayout, fillBrush);
    }

    private void DrawMediaCard(MediaCardItem item, ID2D1DCRenderTarget rt, float y)
    {
        if (!item.IsExpanded)
        {
            DrawMediaCardCollapsed(item, rt, y);
            return;
        }

        const float pillWidth = 400;
        const float pillHeight = 100;
        float pillX = (_width - pillWidth) / 2;
        float pad = 14;
        float opacity = 0.9f;

        // Pill background
        using var bgBrush = rt.CreateSolidColorBrush(new Color4(0, 0, 0, 0.65f * opacity));
        var pillRR = new RoundedRectangle(new RectangleF(pillX, y, pillWidth, pillHeight), 20, 20);
        rt.FillRoundedRectangle(ref pillRR, bgBrush);

        float cx = pillX + pad;

        // Cover / music note icon
        if (item.CoverBitmap != null)
        {
            // 使用 render target transform 定位+缩放，避免 Rect 参数问题
            var oldTransform = rt.Transform;
            float coverSize = 64;
            var bmpSize = item.CoverBitmap.Size;
            float scale = coverSize / Math.Max(bmpSize.Width, bmpSize.Height);
            rt.Transform = Matrix3x2.CreateScale(scale, scale) * Matrix3x2.CreateTranslation(cx, y + 10);
            rt.DrawBitmap(item.CoverBitmap, opacity, BitmapInterpolationMode.Linear);
            rt.Transform = oldTransform;
            cx += 72;
        }
        else
        {
            using var noteFormat = _dwFactory.CreateTextFormat("Segoe UI", null,
                DWriteFontWeight.Normal, DWriteFontStyle.Normal, DWriteFontStretch.Normal, 26);
            using var noteLayout = _dwFactory.CreateTextLayout("\uD83C\uDFB5", noteFormat, 36, 36);
            using var noteBrush = rt.CreateSolidColorBrush(new Color4(1, 1, 1, opacity * 0.5f));
            rt.DrawTextLayout(new Vector2(cx + 4, y + 30), noteLayout, noteBrush);
            cx += 46;
        }

        float textW = pillWidth - pad - (cx - pillX) - 50;

        // Title
        string title = string.IsNullOrEmpty(item.Title) ? "未在播放" : item.Title;
        using var titleFmt = _dwFactory.CreateTextFormat("Microsoft YaHei", null,
            DWriteFontWeight.Bold, DWriteFontStyle.Normal, DWriteFontStretch.Normal, 16);
        using var titleLyt = _dwFactory.CreateTextLayout(title, titleFmt, textW, 24);
        titleLyt.WordWrapping = WordWrapping.NoWrap;
        titleLyt.SetTrimming(new Trimming { Delimiter = 0, DelimiterCount = 0 }, null!);
        using var titleBr = rt.CreateSolidColorBrush(new Color4(1, 1, 1, opacity));
        rt.DrawTextLayout(new Vector2(cx, y + 10), titleLyt, titleBr);

        // Artist
        if (!string.IsNullOrEmpty(item.Artist))
        {
            using var artFmt = _dwFactory.CreateTextFormat("Microsoft YaHei", null,
                DWriteFontWeight.Normal, DWriteFontStyle.Normal, DWriteFontStretch.Normal, 12);
            using var artLyt = _dwFactory.CreateTextLayout(item.Artist, artFmt, textW, 20);
            artLyt.WordWrapping = WordWrapping.NoWrap;
            artLyt.SetTrimming(new Trimming { Delimiter = 0, DelimiterCount = 0 }, null!);
            using var artBr = rt.CreateSolidColorBrush(new Color4(0.75f, 0.75f, 0.75f, opacity));
            rt.DrawTextLayout(new Vector2(cx, y + 36), artLyt, artBr);
        }

        // Play/pause button
        string playIcon = item.IsPlaying ? "\u23F8" : "\u25B6";
        using var playFmt = _dwFactory.CreateTextFormat("Segoe UI", null,
            DWriteFontWeight.Normal, DWriteFontStyle.Normal, DWriteFontStretch.Normal, 22);
        using var playLyt = _dwFactory.CreateTextLayout(playIcon, playFmt, 30, 30);
        using var playBr = rt.CreateSolidColorBrush(new Color4(1, 1, 1, opacity));
        rt.DrawTextLayout(new Vector2(pillX + pillWidth - pad - 36, y + 14), playLyt, playBr);

        // Progress bar
        using var progBg = rt.CreateSolidColorBrush(new Color4(0.35f, 0.35f, 0.35f, opacity * 0.6f));
        float progY = y + pillHeight - 12;
        float progW = pillWidth - pad * 2;
        var progBgRR = new RoundedRectangle(new RectangleF(pillX + pad, progY, progW, 4), 2, 2);
        rt.FillRoundedRectangle(ref progBgRR, progBg);

        using var progFill = rt.CreateSolidColorBrush(new Color4(0.3f, 0.7f, 1.0f, opacity));
        float fillW = progW * 0.35f;
        var progFillRR = new RoundedRectangle(new RectangleF(pillX + pad, progY, fillW, 4), 2, 2);
        rt.FillRoundedRectangle(ref progFillRR, progFill);
    }

    /// <summary>
    /// 收起态：紧凑胶囊 — 小封面 + 标题 + 播放频谱指示器
    /// </summary>
    private void DrawMediaCardCollapsed(MediaCardItem item, ID2D1DCRenderTarget rt, float y)
    {
        const float pillHeight = 36;
        float pad = 8;
        float opacity = 0.9f;

        // 计算内容宽度：封面(24) + 间距(6) + 标题(动态) + 间距(6) + 频谱5条(23)
        // 先测量标题文本宽度
        string titleText = string.IsNullOrEmpty(item.Title) ? "未在播放" : item.Title;
        using var titleFmt = _dwFactory.CreateTextFormat("Microsoft YaHei", null,
            DWriteFontWeight.Normal, DWriteFontStyle.Normal, DWriteFontStretch.Normal, 12);
        using var titleMeasure = _dwFactory.CreateTextLayout(titleText, titleFmt, 300, 20);
        titleMeasure.WordWrapping = WordWrapping.NoWrap;
        var titleMetrics = titleMeasure.Metrics;
        float titleWidth = Math.Min(titleMetrics.WidthIncludingTrailingWhitespace, 180);

        float contentWidth = 24 + 6 + titleWidth + 6 + 21; // 5条频谱: 5*2.5+4*2=20.5
        float pillWidth = Math.Max(contentWidth + pad * 2, 120);
        float pillX = (_width - pillWidth) / 2;

        // Pill background
        using var bgBrush = rt.CreateSolidColorBrush(new Color4(0, 0, 0, 0.65f * opacity));
        var pillRR = new RoundedRectangle(new RectangleF(pillX, y, pillWidth, pillHeight), 16, 16);
        rt.FillRoundedRectangle(ref pillRR, bgBrush);

        float cx = pillX + pad;
        float centerY = y + (pillHeight - 24) / 2.0f;

        // 小封面 (24x24)
        if (item.CoverBitmap != null)
        {
            var oldTransform = rt.Transform;
            float coverSize = 24;
            var bmpSize = item.CoverBitmap.Size;
            float scale = coverSize / Math.Max(bmpSize.Width, bmpSize.Height);
            rt.Transform = Matrix3x2.CreateScale(scale, scale) * Matrix3x2.CreateTranslation(cx, centerY);
            rt.DrawBitmap(item.CoverBitmap, opacity, BitmapInterpolationMode.Linear);
            rt.Transform = oldTransform;
        }
        else
        {
            // 音符图标替代
            using var noteFormat = _dwFactory.CreateTextFormat("Segoe UI", null,
                DWriteFontWeight.Normal, DWriteFontStyle.Normal, DWriteFontStretch.Normal, 14);
            using var noteLayout = _dwFactory.CreateTextLayout("\uD83C\uDFB5", noteFormat, 24, 24);
            using var noteBrush = rt.CreateSolidColorBrush(new Color4(1, 1, 1, opacity * 0.5f));
            rt.DrawTextLayout(new Vector2(cx, centerY), noteLayout, noteBrush);
        }
        cx += 24 + 6;

        // 标题文本（单行，超出截断）
        using var titleDrawLyt = _dwFactory.CreateTextLayout(titleText, titleFmt, titleWidth, 20);
        titleDrawLyt.WordWrapping = WordWrapping.NoWrap;
        titleDrawLyt.SetTrimming(new Trimming { Delimiter = 0, DelimiterCount = 0 }, null!);
        using var titleBrush = rt.CreateSolidColorBrush(new Color4(1, 1, 1, opacity));
        rt.DrawTextLayout(new Vector2(cx, centerY + 2), titleDrawLyt, titleBrush);
        cx += titleWidth + 6;

        // 播放频谱指示器（5 个小竖条，双波峰W形流畅震荡动画，居中向两端缩放）
        const int barCount = 5;
        float barWidth = 2.5f;
        float barGap = 2;
        float maxBarHeight = 14;
        float barTop = centerY + (24 - maxBarHeight) / 2.0f; // 垂直居中
        using var barBrush = rt.CreateSolidColorBrush(new Color4(0.3f, 0.7f, 1.0f, opacity));

        if (item.IsPlaying)
        {
            // 双波峰 W 形震荡动画：bars 1和3为波峰，bar 2为波谷，bars 0和4为边缘
            double freq = Stopwatch.Frequency;
            double now = Stopwatch.GetTimestamp();
            double elapsed = (now - item.StartTime) / freq;

            float[] heights = new float[barCount];
            float phase = (float)(elapsed * 3.5); // 震荡速度

            for (int i = 0; i < barCount; i++)
            {
                // 两个波峰位于 bar 1 和 bar 3，用距离最近波峰的距离决定基础高度
                double dist = Math.Min(Math.Abs(i - 1.0), Math.Abs(i - 3.0));
                // 离波峰越远越低：peak=1.0, mid=0.65, edge=0.35
                double baseFactor = 1.0 - dist * 0.35;

                // 每根条独立的相位震荡，产生流动感
                double osc = 0.55 + 0.45 * Math.Sin(phase + i * 1.1);
                double h = maxBarHeight * baseFactor * osc;

                heights[i] = (float)Math.Max(1.0, h);
            }

            for (int i = 0; i < barCount; i++)
            {
                float bx = cx + i * (barWidth + barGap);
                float h = heights[i];
                float top = barTop + (maxBarHeight - h) / 2.0f; // 从中间向两端缩放
                int bw = Math.Max(1, (int)(barWidth));
                int bh = Math.Max(1, (int)(h));
                var barRect = new Vortice.Mathematics.Rect((int)bx, (int)top, bw, bh);
                rt.FillRectangle(in barRect, barBrush);
            }
        }
        else
        {
            // 暂停时：统一低高度，居中
            float h = maxBarHeight * 0.2f;
            float top = barTop + (maxBarHeight - h) / 2.0f;
            for (int i = 0; i < barCount; i++)
            {
                float bx = cx + i * (barWidth + barGap);
                int bw = Math.Max(1, (int)(barWidth));
                int bh = Math.Max(1, (int)(h));
                var barRect = new Vortice.Mathematics.Rect((int)bx, (int)top, bw, bh);
                rt.FillRectangle(in barRect, barBrush);
            }
        }
    }

    private void DrawSuperIslandCard(SuperIslandItem item, ID2D1DCRenderTarget rt, float y)
    {
        const float pillWidth = 380;
        float pillHeight = item.IsExpanded && item.State.HasExtra ? 102 : 76;
        float pillX = (_width - pillWidth) / 2;
        float pad = 14;
        float opacity = 0.9f;

        // Pill background
        using var bgBrush = rt.CreateSolidColorBrush(new Color4(0, 0, 0, 0.65f * opacity));
        var pillRR = new RoundedRectangle(new RectangleF(pillX, y, pillWidth, pillHeight), 16, 16);
        rt.FillRoundedRectangle(ref pillRR, bgBrush);

        float cx = pillX + pad;
        float textW = pillWidth - pad * 2;

        // Icon (28x28)
        if (item.IconBitmap != null)
        {
            var iconRect = new Vortice.Mathematics.Rect((int)cx, (int)y + 10, 28, 28);
            rt.DrawBitmap(item.IconBitmap, opacity, BitmapInterpolationMode.Linear, iconRect);
            cx += 36;
            textW -= 36;
        }

        // Title + Subtitle in one line
        string titleText = item.State.Title ?? "";
        if (!string.IsNullOrEmpty(item.State.Subtitle))
            titleText = string.IsNullOrEmpty(titleText) ? item.State.Subtitle : $"{titleText} · {item.State.Subtitle}";

        using var titleFmt = _dwFactory.CreateTextFormat("Microsoft YaHei", null,
            DWriteFontWeight.SemiBold, DWriteFontStyle.Normal, DWriteFontStretch.Normal, 14);
        float titleMaxW = textW - 80; // 留出右侧计时器空间
        using var titleLyt = _dwFactory.CreateTextLayout(titleText, titleFmt, Math.Max(titleMaxW, 60), 22);
        using var titleBr = rt.CreateSolidColorBrush(new Color4(1, 1, 1, opacity));
        rt.DrawTextLayout(new Vector2(cx, y + 10), titleLyt, titleBr);

        float nextLineY = y + 34; // 第二行起始 Y

        // Timer（右侧）
        string timerText = item.State.GetDisplayTime();
        string? progressText = item.State.GetProgressText();
        string rightText = !string.IsNullOrEmpty(timerText) ? timerText : progressText ?? "";
        if (!string.IsNullOrEmpty(rightText))
        {
            using var timeFmt = _dwFactory.CreateTextFormat("Microsoft YaHei", null,
                DWriteFontWeight.Normal, DWriteFontStyle.Normal, DWriteFontStretch.Normal, 11);
            using var timeLyt = _dwFactory.CreateTextLayout(rightText, timeFmt, 70, 16);
            using var timeBr = rt.CreateSolidColorBrush(new Color4(0.7f, 0.7f, 0.7f, opacity));
            rt.DrawTextLayout(new Vector2(pillX + pillWidth - pad - 70, y + 11), timeLyt, timeBr);
        }

        // Additional text line（第二行）
        if (!string.IsNullOrEmpty(item.State.AdditionalText))
        {
            using var addFmt = _dwFactory.CreateTextFormat("Microsoft YaHei", null,
                DWriteFontWeight.Normal, DWriteFontStyle.Normal, DWriteFontStretch.Normal, 11);
            using var addLyt = _dwFactory.CreateTextLayout(item.State.AdditionalText, addFmt, textW, 18);
            using var addBr = rt.CreateSolidColorBrush(new Color4(0.7f, 0.7f, 0.7f, opacity));
            rt.DrawTextLayout(new Vector2(cx, nextLineY), addLyt, addBr);
        }

        // Extra line（展开时第三行，从 ParamV2 解析出的结构化信息）
        if (item.IsExpanded && item.State.HasExtra)
        {
            float extraY = string.IsNullOrEmpty(item.State.AdditionalText) ? nextLineY : nextLineY + 20;
            if (item.ExtraLayout != null)
            {
                using var extraBr = rt.CreateSolidColorBrush(new Color4(0.6f, 0.8f, 1.0f, opacity));
                rt.DrawTextLayout(new Vector2(cx, extraY), item.ExtraLayout, extraBr);
            }
        }

        // Progress bar（底部）
        if (item.State.HasProgress)
        {
            using var progBg = rt.CreateSolidColorBrush(new Color4(0.35f, 0.35f, 0.35f, opacity * 0.6f));
            float progY = y + pillHeight - 10;
            float progW = pillWidth - pad * 2;
            var progBgRR = new RoundedRectangle(new RectangleF(pillX + pad, progY, progW, 3), 1.5f, 1.5f);
            rt.FillRoundedRectangle(ref progBgRR, progBg);

            float pct = Math.Clamp(item.State.Progress / 100f, 0f, 1f);
            float fillW = progW * pct;
            using var progFill = rt.CreateSolidColorBrush(new Color4(0.3f, 0.7f, 1.0f, opacity));
            var progFillRR = new RoundedRectangle(new RectangleF(pillX + pad, progY, fillW, 3), 1.5f, 1.5f);
            rt.FillRoundedRectangle(ref progFillRR, progFill);
        }
    }

    private void AssignTrack(DanmakuItem item)
    {
        var s = _currentStyle;
        double trackHeight = s.FontSize + 24;
        int totalTracks = Math.Max(1, (int)(_height / trackHeight));
        int activeCount = Math.Clamp(
            (int)(totalTracks * (s.DisplayAreaPercent / 100.0)), 1, totalTracks);

        double minGap = s.Density switch { 1 => 20, 2 => -300, _ => 100 };
        double now = Stopwatch.GetTimestamp();
        double freq = Stopwatch.Frequency;

        var available = new List<int>();
        for (int i = 0; i < activeCount; i++)
        {
            bool occupied = false;
            foreach (var existing in _items.OfType<DanmakuItem>())
            {
                if (existing.TrackIndex != i || !existing.Active) continue;
                double elapsed = (now - existing.StartTime) / freq;
                double rightEdge = existing.SpawnX
                    - elapsed * existing.Settings.PixelsPerSecond + existing.TotalWidth;
                if (rightEdge > _width - minGap) { occupied = true; break; }
            }
            if (!occupied) available.Add(i);
        }

        int track = available.Count > 0
            ? available[Random.Shared.Next(available.Count)]
            : Random.Shared.Next(0, activeCount);

        item.TrackIndex = track;
        item.TrackY = (float)(260 + track * trackHeight);
        item.SpawnX = _width;
    }

    private ID2D1Bitmap? LoadBitmapFromPng(byte[] pngData, ID2D1DCRenderTarget rt)
    {
        using var stream = _wicFactory.CreateStream();
        stream.Initialize(pngData);
        using var decoder = _wicFactory.CreateDecoderFromStream(stream, DecodeOptions.CacheOnLoad);
        using var frame = decoder.GetFrame(0);
        using var converter = _wicFactory.CreateFormatConverter();
        converter.Initialize(frame, Vortice.WIC.PixelFormat.Format32bppPBGRA);

        var size = converter.Size;
        int stride = size.Width * 4;
        byte[] pixels = new byte[stride * size.Height];

        unsafe
        {
            fixed (byte* pPixels = pixels)
            {
                converter.CopyPixels(new RectI(0, 0, size.Width, size.Height), (uint)stride, (uint)pixels.Length, (IntPtr)pPixels);
            }
        }

        var props = new BitmapProperties(new Vortice.DCommon.PixelFormat(
            Vortice.DXGI.Format.B8G8R8A8_UNorm,
            Vortice.DCommon.AlphaMode.Premultiplied));
        var bitmap = rt.CreateBitmap(size, props);

        unsafe
        {
            fixed (byte* pPixels = pixels)
            {
                bitmap.CopyFromMemory((IntPtr)pPixels, (uint)stride);
            }
        }
        return bitmap;
    }

    public void Dispose()
    {
        Stop();
        _wicFactory.Dispose();
        _dwFactory.Dispose();
        _d2dFactory.Dispose();
    }

    #region P/Invoke

    private delegate IntPtr WndProcDelegate(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    private const uint WS_POPUP = 0x80000000;
    private const uint WS_EX_LAYERED = 0x00080000;
    private const uint WS_EX_TRANSPARENT = 0x00000020;
    private const uint WS_EX_TOPMOST = 0x00000008;
    private const uint WS_EX_TOOLWINDOW = 0x00000080;
    private const uint WS_EX_NOACTIVATE = 0x08000000;
    private const int SW_SHOWNOACTIVATE = 4;
    private const uint SWP_NOMOVE = 0x0002;
    private const uint SWP_NOSIZE = 0x0001;
    private const uint SWP_NOACTIVATE = 0x0010;
    private const int SM_CXSCREEN = 0;
    private const int SM_CYSCREEN = 1;
    private const int BI_RGB = 0;
    private const uint DIB_RGB_COLORS = 0;
    private const byte AC_SRC_OVER = 0;
    private const byte AC_SRC_ALPHA = 1;
    private const uint ULW_ALPHA = 2;
    private static readonly IntPtr HWND_TOPMOST = new(-1);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WNDCLASSW
    {
        public uint style;
        public IntPtr lpfnWndProc;
        public int cbClsExtra;
        public int cbWndExtra;
        public IntPtr hInstance;
        public IntPtr hIcon;
        public IntPtr hCursor;
        public IntPtr hbrBackground;
        public string? lpszMenuName;
        public string lpszClassName;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT { public int X, Y; public POINT(int x, int y) { X = x; Y = y; } }

    [StructLayout(LayoutKind.Sequential)]
    private struct SIZE { public int CX, CY; public SIZE(int cx, int cy) { CX = cx; CY = cy; } }

    [StructLayout(LayoutKind.Sequential)]
    private struct BLENDFUNCTION
    {
        public byte BlendOp, BlendFlags, SourceConstantAlpha, AlphaFormat;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BITMAPINFOHEADER
    {
        public int biSize, biWidth, biHeight;
        public short biPlanes, biBitCount;
        public int biCompression, biSizeImage, biXPelsPerMeter, biYPelsPerMeter, biClrUsed, biClrImportant;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BITMAPINFO { public BITMAPINFOHEADER bmiHeader; }

    [StructLayout(LayoutKind.Sequential)]
    private struct MSG
    {
        public IntPtr hwnd; public uint message; public IntPtr wParam, lParam;
        public uint time; public int pt_x, pt_y;
    }

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern ushort RegisterClassW(ref WNDCLASSW wc);

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern IntPtr CreateWindowExW(
        uint dwExStyle, string lpClassName, string lpWindowName, uint dwStyle,
        int x, int y, int nWidth, int nHeight,
        IntPtr hWndParent, IntPtr hMenu, IntPtr hInstance, IntPtr lpParam);

    [DllImport("user32.dll")]
    private static extern bool DestroyWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter,
        int X, int Y, int cx, int cy, uint uFlags);

    [DllImport("user32.dll")]
    private static extern bool UpdateLayeredWindow(IntPtr hWnd, IntPtr hdcDst,
        ref POINT pptDst, ref SIZE psize, IntPtr hdcSrc, ref POINT pptSrc,
        uint crKey, ref BLENDFUNCTION pblend, uint dwFlags);

    [DllImport("user32.dll")]
    private static extern IntPtr GetDC(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);

    [DllImport("user32.dll")]
    private static extern IntPtr DefWindowProcW(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool PeekMessageW(out MSG lpMsg, IntPtr hWnd, uint wMsgFilterMin, uint wMsgFilterMax, uint wRemoveMsg);

    [DllImport("user32.dll")]
    private static extern bool TranslateMessage(ref MSG lpMsg);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr DispatchMessageW(ref MSG lpMsg);

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateCompatibleDC(IntPtr hdc);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteDC(IntPtr hdc);

    [DllImport("gdi32.dll")]
    private static extern IntPtr SelectObject(IntPtr hdc, IntPtr h);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteObject(IntPtr ho);

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateDIBSection(IntPtr hdc, ref BITMAPINFO pbmi,
        uint usage, out IntPtr ppvBits, IntPtr hSection, uint offset);

    [DllImport("dwmapi.dll")]
    private static extern int DwmFlush();

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr GetModuleHandleW(string? lpModuleName);

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int nIndex);

    private static IntPtr WndProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam)
        => DefWindowProcW(hWnd, msg, wParam, lParam);

    #endregion
}
