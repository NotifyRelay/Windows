using System.Numerics;
using NotifyRelay.Models.Render;
using Vortice.Direct2D1;
using Vortice.Mathematics;
using DWriteFontWeight = Vortice.DirectWrite.FontWeight;
using DWriteTextAlignment = Vortice.DirectWrite.TextAlignment;
using D2DBezierSegment = Vortice.Direct2D1.BezierSegment;

namespace NotifyRelay.Services.Overlay.UI.Elements;

/// <summary>
/// 心率元素：文本 / 胶囊卡片 / 心形+曲线 三种形态可组合。
/// 声明式形态：
/// <c>Align(center) { Column(crossAlign=Center) { Stack { Canvas(心形+曲线), Text(bpm 居中) } ; Surface(胶囊)│Text } }</c>
/// 心跳缩放取 <see cref="PaintScope.NowSeconds"/>，不触发重组。
/// </summary>
internal sealed class HeartRateElement : IOverlayElement
{
    private const float Opacity = 0.92f;
    private const float HeartSizeBase = 110f;
    private const float LineFontSize = 18f;
    private const float LineHeight = 26f;
    private const float CardPadX = 14f;
    private const float CardPadY = 6f;
    private const float HeartTextGap = 6f;
    private const int HistoryMax = 60;

    private readonly ElementContext _ctx;

    // 状态（_lock 保护）
    private bool _enabled;
    private int _styleFlags = 1;
    private string _targetScreen = "PRIMARY";
    private float _xPct = 90f;
    private float _yPct = 85f;
    private byte _colorR = 255, _colorG = 255, _colorB = 255;
    private float _outlineWidth = 2f;
    private float _scale = 1f;
    private bool _alertEnabled;
    private bool _hideWhenDisconnected = true;
    private int _lowAlert = 50;
    private int _highAlert = 120;
    private int _spikeDelta = 20;
    private int _bpm = -1;
    private bool _connected;
    private readonly List<int> _history = [];

    // 心形几何缓存（设备无关资源，0..1 单位空间）
    private ID2D1PathGeometry? _heartGeometry;

    public HeartRateElement(ElementContext ctx) => _ctx = ctx;

    public string Name => "HeartRate";

    public void LoadSettings(IOverlaySettings s)
        => SetConfig(s.HeartRateOverlayEnabled, s.HeartRateStyle, s.HeartRateTargetScreen,
            s.HeartRateXPercent, s.HeartRateYPercent, s.HeartRateColor, s.HeartRateTextOutlineWidth,
            s.HeartRateScale, s.HeartRateAlertEnabled, s.HeartRateLowAlert, s.HeartRateHighAlert,
            s.HeartRateSpikeDelta, s.HeartRateHideWhenDisconnected);

    /// <summary>更新心率覆盖层配置（启用、样式组合、目标屏、位置百分比、颜色）。</summary>
    public void SetConfig(bool enabled, int styleFlags, string targetScreen, float xPct, float yPct,
        string colorHex, float outlineWidth, float scale, bool alertEnabled, int lowAlert, int highAlert,
        int spikeDelta, bool hideWhenDisconnected = true)
        => _ctx.WithLock(() =>
        {
            _enabled = enabled;
            _styleFlags = styleFlags;
            _targetScreen = string.IsNullOrEmpty(targetScreen) ? "PRIMARY" : targetScreen;
            _xPct = Math.Clamp(xPct, 0f, 100f);
            _yPct = Math.Clamp(yPct, 0f, 100f);
            _colorR = ColorHexParser.ParseChannel(colorHex, 255, 0);
            _colorG = ColorHexParser.ParseChannel(colorHex, 255, 2);
            _colorB = ColorHexParser.ParseChannel(colorHex, 255, 4);
            _outlineWidth = Math.Clamp(outlineWidth, 0.1f, 3f);
            _scale = Math.Clamp(scale, 0.5f, 2f);
            _alertEnabled = alertEnabled;
            _lowAlert = lowAlert;
            _highAlert = highAlert;
            _spikeDelta = spikeDelta;
            _hideWhenDisconnected = hideWhenDisconnected;
            if (!enabled)
            {
                // 关闭显示时清空历史，避免下次开启残留旧曲线
                _history.Clear();
            }
        }, "覆盖层数据锁获取超时，跳过心率配置更新");

    /// <summary>推送最新心率值（BLE 通知回调线程调用）。</summary>
    public void UpdateHeartRate(int bpm)
    {
        if (bpm <= 0) return;
        // 最小单位 5 bpm，避免过小波动导致统计图剧烈变化
        int q = (int)Math.Round(bpm / 5.0) * 5;
        if (q <= 0) return;
        _ctx.WithLock(() =>
        {
            _bpm = bpm;              // 显示与异常判定使用原始值
            _history.Add(q);         // 统计图使用量化值
            if (_history.Count > HistoryMax) _history.RemoveAt(0);
        }, string.Empty);
    }

    /// <summary>设置心率设备连接状态；断开时清空当前值与历史。</summary>
    public void SetConnected(bool connected)
        => _ctx.WithLock(() =>
        {
            _connected = connected;
            if (!connected)
            {
                _bpm = -1;
                _history.Clear();
            }
        }, "覆盖层数据锁获取超时，跳过心率连接状态更新");

    /// <summary>清空心率显示数据。</summary>
    public void Clear()
        => _ctx.WithLock(() =>
        {
            _bpm = -1;
            _history.Clear();
        }, "覆盖层数据锁获取超时，跳过心率数据清空");

    public bool IsActive()
        => _ctx.WithLock(() => _enabled && (!_hideWhenDisconnected || _connected), false);

    public bool IsTargetScreen(ScreenOverlay o)
    {
        if (o.IsSpan) return false;   // 心率元素不支持跨屏窗口
        var target = _ctx.WithLock<string?>(() =>
            _enabled && (!_hideWhenDisconnected || _connected) ? _targetScreen : null, null);
        if (target == null) return false;
        return _ctx.IsTargetScreen(o, target, allowSpan: false);
    }

    public void Compose(OverlayComposer composer, ScreenOverlay o)
    {
        int bpm, flags;
        float xPct, yPct, outlineW, scale;
        Color4 textColor, strokeColor;
        int[] history;
        bool alert, connected;
        lock (_ctx.StateLock)
        {
            if (!_enabled || (_hideWhenDisconnected && !_connected)) return;
            bpm = _bpm;
            flags = _styleFlags;
            xPct = _xPct;
            yPct = _yPct;
            textColor = new Color4(_colorR / 255f, _colorG / 255f, _colorB / 255f, 1f);
            // 描边色为文本色反色
            strokeColor = new Color4((255 - _colorR) / 255f, (255 - _colorG) / 255f, (255 - _colorB) / 255f, 1f);
            outlineW = _outlineWidth;
            scale = _scale;
            alert = ComputeAlertLocked(bpm);
            history = [.. _history];
            connected = _connected;
        }

        bool showText = (flags & 1) != 0;
        bool showCard = (flags & 2) != 0;
        bool showHeart = (flags & 4) != 0;
        if (!showText && !showCard && !showHeart) showText = true;

        // 连接状态与心率值取自同一把锁下的同一次快照，避免两次读取间状态跳变
        bool hasData = connected && bpm > 0;
        var bpmText = hasData ? bpm.ToString() : "--";
        var line = hasData ? $"\u2764 {bpm} BPM" : "\u2764 -- 未连接";

        float fontSize = LineFontSize * scale;
        float lineH = LineHeight * scale;
        float heartSize = HeartSizeBase * scale;
        float padX = CardPadX * scale;
        float padY = CardPadY * scale;
        float effOutline = outlineW * scale;
        float gap = HeartTextGap * scale;

        composer.Node<Align>(null, a =>
        {
            a.XPct = xPct;
            a.YPct = yPct;
            a.AnchorAtCenterX = true; a.AnchorAtCenterY = true;
            a.ClampToBounds = true;
        }, () =>
        {
            composer.Node<Column>("content", col =>
            {
                col.CrossAlignment = CrossAlignment.Center;
                col.Spacing = showHeart && (showText || showCard) ? gap : 0f;
            }, () =>
            {
                if (showHeart)
                {
                    // 心形 + BPM 数字 + 迷你曲线：层叠 + 绝对定位，走 Canvas 逃生节点
                    int[] historySnapshot = history;
                    bool alertSnapshot = alert;
                    composer.Leaf<Canvas>("heart", cv =>
                    {
                        cv.FixedSize = new Size(heartSize, heartSize);
                        cv.OnPaint = (s, r) => PaintHeart(s, r, heartSize, bpmText, historySnapshot, alertSnapshot);
                    });
                }

                if (showText || showCard)
                {
                    if (showCard)
                    {
                        composer.Node<Surface>("card", surface =>
                        {
                            surface.Radius = (lineH + padY * 2) / 2f;
                            surface.Filled = true;
                            surface.Fill = new Color4(0f, 0f, 0f, 0.65f * Opacity);
                            surface.Insets = new Insets(padX, padY);
                        }, () =>
                        {
                            ComposeLine(composer, line, fontSize, lineH, textColor, strokeColor,
                                effOutline, showText, Opacity);
                        });
                    }
                    else
                    {
                        ComposeLine(composer, line, fontSize, lineH, textColor, strokeColor,
                            effOutline, showText, Opacity);
                    }
                }
            });
        });
    }

    private static void ComposeLine(OverlayComposer composer, string line, float fontSize, float lineH,
        Color4 textColor, Color4 strokeColor, float effOutline, bool showOutline, float opacity)
        => composer.Leaf<Text>("line", t =>
        {
            t.TextValue = line;
            t.FontFamily = "Microsoft YaHei";
            t.Weight = DWriteFontWeight.SemiBold;
            t.FontSize = fontSize;
            t.LineHeight = lineH;
            t.ReportedHeight = lineH;
            t.Color = new Color4(textColor.R, textColor.G, textColor.B, opacity);
            t.OutlineWidth = showOutline ? effOutline : 0f;
            t.OutlineColor = new Color4(strokeColor.R, strokeColor.G, strokeColor.B, opacity);
            t.Ellipsis = false;
        });

    /// <summary>异常加速判定（调用方须持有 <see cref="_ctx"/>.StateLock）。</summary>
    private bool ComputeAlertLocked(int bpm)
    {
        if (!_alertEnabled || !_connected || bpm <= 0) return false;
        if (bpm < _lowAlert || bpm > _highAlert) return true;
        if (_history.Count < 5) return false;
        int n = Math.Min(_history.Count, 10);
        int sum = 0;
        for (int i = _history.Count - n; i < _history.Count; i++) sum += _history[i];
        return bpm - sum / n >= _spikeDelta;
    }

    /// <summary>绘制心形 + 居中 BPM 数字 + 底部迷你曲线。</summary>
    private void PaintHeart(PaintScope s, Rect rect, float size, string bpmText, int[] history, bool alert)
    {
        EnsureHeartGeometry();
        if (_heartGeometry == null) return;

        float x = rect.X;
        float y = rect.Y;
        float beatScale = ComputeBeatScale(s.NowSeconds, alert);
        float drawSize = size * beatScale;
        float offset = (size - drawSize) / 2f;

        // 心形填充（单位几何缩放平移，围绕中心缩放）
        s.PushTransform(Matrix3x2.CreateScale(drawSize, drawSize)
            * Matrix3x2.CreateTranslation(x + offset, y + offset));
        try
        {
            using var heartBrush = s.Rt.CreateSolidColorBrush(new Color4(0.906f, 0.282f, 0.231f, Opacity)); // #E7483B
            s.Rt.FillGeometry(_heartGeometry, heartBrush);
        }
        finally
        {
            s.PopTransform();
        }

        // BPM 数字居中（心形视觉中心约在 40% 高度处）
        using (var numFmt = s.CreateTextFormat("Segoe UI", DWriteFontWeight.Bold, drawSize * 0.26f))
        using (var numLayout = s.DwFactory.CreateTextLayout(bpmText, numFmt, drawSize, drawSize * 0.4f))
        {
            numLayout.TextAlignment = DWriteTextAlignment.Center;
            using var numBrush = s.Rt.CreateSolidColorBrush(new Color4(1, 1, 1, Opacity));
            s.Rt.DrawTextLayout(new Vector2(x + offset, y + offset + drawSize * 0.24f), numLayout, numBrush);
        }

        // 迷你心率曲线（心形中下部，55%~72% 高度带内）
        if (history.Length < 2) return;

        int min = int.MaxValue, max = int.MinValue;
        foreach (var v in history)
        {
            if (v < min) min = v;
            if (v > max) max = v;
        }
        int range = Math.Max(max - min, 10);   // 避免平线时除零/过度放大

        float bandLeft = x + offset + drawSize * 0.28f;
        float bandWidth = drawSize * 0.44f;
        float bandTop = y + offset + drawSize * 0.55f;
        float bandHeight = drawSize * 0.17f;

        using var lineBrush = s.Rt.CreateSolidColorBrush(new Color4(1, 1, 1, Opacity * 0.85f));
        float stepX = bandWidth / (history.Length - 1);
        for (int i = 1; i < history.Length; i++)
        {
            float x0 = bandLeft + (i - 1) * stepX;
            float x1 = bandLeft + i * stepX;
            float y0 = bandTop + bandHeight * (1f - (history[i - 1] - min) / (float)range);
            float y1 = bandTop + bandHeight * (1f - (history[i] - min) / (float)range);
            s.Rt.DrawLine(new Vector2(x0, y0), new Vector2(x1, y1), lineBrush,
                Math.Max(1f, drawSize * 0.014f));
        }
    }

    /// <summary>构建单位空间（0..1）心形路径几何并缓存。</summary>
    private void EnsureHeartGeometry()
    {
        if (_heartGeometry != null) return;
        var geometry = _ctx.D2DFactory.CreatePathGeometry();
        using (var sink = geometry.Open())
        {
            sink.BeginFigure(new Vector2(0.5f, 0.3f), FigureBegin.Filled);
            sink.AddBezier(new D2DBezierSegment
            {
                Point1 = new Vector2(0.5f, 0.22f),
                Point2 = new Vector2(0.42f, 0.05f),
                Point3 = new Vector2(0.25f, 0.05f)
            });
            sink.AddBezier(new D2DBezierSegment
            {
                Point1 = new Vector2(0.05f, 0.05f),
                Point2 = new Vector2(0.0f, 0.25f),
                Point3 = new Vector2(0.0f, 0.35f)
            });
            sink.AddBezier(new D2DBezierSegment
            {
                Point1 = new Vector2(0.0f, 0.55f),
                Point2 = new Vector2(0.2f, 0.75f),
                Point3 = new Vector2(0.5f, 1.0f)
            });
            sink.AddBezier(new D2DBezierSegment
            {
                Point1 = new Vector2(0.8f, 0.75f),
                Point2 = new Vector2(1.0f, 0.55f),
                Point3 = new Vector2(1.0f, 0.35f)
            });
            sink.AddBezier(new D2DBezierSegment
            {
                Point1 = new Vector2(1.0f, 0.25f),
                Point2 = new Vector2(0.95f, 0.05f),
                Point3 = new Vector2(0.75f, 0.05f)
            });
            sink.AddBezier(new D2DBezierSegment
            {
                Point1 = new Vector2(0.58f, 0.05f),
                Point2 = new Vector2(0.5f, 0.22f),
                Point3 = new Vector2(0.5f, 0.3f)
            });
            sink.EndFigure(FigureEnd.Closed);
            sink.Close();
        }
        _heartGeometry = geometry;
    }

    /// <summary>释放与渲染目标无关的几何缓存（窗口清理 / 覆盖层重建时调用，重建时懒重建）。</summary>
    public void Reset()
    {
        _heartGeometry?.Dispose();
        _heartGeometry = null;
    }

    /// <summary>计算心形跳动缩放：正常时轻微脉动；异常时突然加快且幅度更大。</summary>
    private static float ComputeBeatScale(double tSec, bool alert)
    {
        const double Tau = Math.PI * 2.0;
        if (alert)
        {
            double phase = tSec / 0.4 * Tau;   // 周期约 0.4s，明显加快
            return 1f + 0.11f * (float)(0.5 - 0.5 * Math.Cos(phase));   // 0.89~1.11
        }
        double phase2 = tSec / 1.1 * Tau;      // 周期约 1.1s，轻微
        return 1f + 0.04f * (float)(0.5 - 0.5 * Math.Cos(phase2));      // 0.96~1.04
    }
}
