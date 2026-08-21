using System.Numerics;
using Vortice.Direct2D1;
using Vortice.DirectWrite;
using Vortice.Mathematics;
using DWriteFontWeight = Vortice.DirectWrite.FontWeight;
using DWriteTextAlignment = Vortice.DirectWrite.TextAlignment;
using D2DBezierSegment = Vortice.Direct2D1.BezierSegment;

namespace NotifyRelay.Services.Overlay;

public partial class OverlayRenderService
{
    // 心率覆盖层状态（_lock 保护）
    private bool _hrEnabled;
    private int _hrStyleFlags = 1;          // 1=文本 2=卡片 4=心形（可组合）
    private string _hrTargetScreen = "PRIMARY";
    private float _hrXPct = 90f;
    private float _hrYPct = 85f;
    private byte _hrColorR = 255, _hrColorG = 255, _hrColorB = 255;
    private float _hrOutlineWidth = 2f;     // 简洁文本描边粗细（像素，0.1~3）
    private float _hrScale = 1f;            // 整体显示大小缩放（0.5~2）
    private bool _hrAlertEnabled;           // 异常时心跳加速开关
    private bool _hrHideWhenDisconnected = true; // 未连接时不显示心率元素
    private int _hrLowAlert = 50;           // 心率过低阈值（启用异常加速时生效）
    private int _hrHighAlert = 120;         // 心率过高阈值（启用异常加速时生效）
    private int _hrSpikeDelta = 20;         // 相对近期均值骤升阈值
    private int _hrBpm = -1;                // -1 = 无数据
    private bool _hrConnected;
    private readonly List<int> _hrHistory = [];
    private const int HrHistoryMax = 60;

    // 心形几何缓存（设备无关资源，0..1 单位空间，绘制时缩放平移）
    private ID2D1PathGeometry? _hrHeartGeometry;

    /// <summary>更新心率覆盖层配置（启用、样式组合、目标屏、位置百分比、颜色）。</summary>
    public void SetHeartRateConfig(bool enabled, int styleFlags, string targetScreen, float xPct, float yPct, string colorHex, float outlineWidth, float scale, bool alertEnabled, int lowAlert, int highAlert, int spikeDelta, bool hideWhenDisconnected = true)
    {
        if (!Monitor.TryEnter(_lock, 2000))
        {
            _logger.LogWarning("覆盖层数据锁获取超时，跳过心率配置更新");
            return;
        }
        try
        {
            _hrEnabled = enabled;
            _hrStyleFlags = styleFlags;
            _hrTargetScreen = string.IsNullOrEmpty(targetScreen) ? "PRIMARY" : targetScreen;
            _hrXPct = Math.Clamp(xPct, 0f, 100f);
            _hrYPct = Math.Clamp(yPct, 0f, 100f);
            _hrColorR = ParseColorChannel(colorHex, 255, 0);
            _hrColorG = ParseColorChannel(colorHex, 255, 2);
            _hrColorB = ParseColorChannel(colorHex, 255, 4);
            _hrOutlineWidth = Math.Clamp(outlineWidth, 0.1f, 3f);
            _hrScale = Math.Clamp(scale, 0.5f, 2f);
            _hrAlertEnabled = alertEnabled;
            _hrLowAlert = lowAlert;
            _hrHighAlert = highAlert;
            _hrSpikeDelta = spikeDelta;
            _hrHideWhenDisconnected = hideWhenDisconnected;
            if (!enabled)
            {
                // 关闭显示时清空历史，避免下次开启残留旧曲线
                _hrHistory.Clear();
            }
        }
        finally
        {
            Monitor.Exit(_lock);
        }
    }

    /// <summary>推送最新心率值（BLE 通知回调线程调用）。</summary>
    public void UpdateHeartRate(int bpm)
    {
        if (bpm <= 0) return;
        // 最小单位 5 bpm，避免过小波动导致统计图剧烈变化
        int q = (int)Math.Round(bpm / 5.0) * 5;
        if (q <= 0) return;
        if (!Monitor.TryEnter(_lock, 2000))
        {
            // 渲染线程异常持锁超时：丢弃本帧数据，避免业务线程无限阻塞
            return;
        }
        try
        {
            _hrBpm = bpm;              // 显示与异常判定使用原始值
            _hrHistory.Add(q);         // 统计图使用量化值（最小单位 5 bpm）
            if (_hrHistory.Count > HrHistoryMax)
                _hrHistory.RemoveAt(0);
        }
        finally
        {
            Monitor.Exit(_lock);
        }
    }

    /// <summary>设置心率设备连接状态；断开时清空当前值与历史。</summary>
    public void SetHeartRateConnected(bool connected)
    {
        if (!Monitor.TryEnter(_lock, 2000))
        {
            _logger.LogWarning("覆盖层数据锁获取超时，跳过心率连接状态更新");
            return;
        }
        try
        {
            _hrConnected = connected;
            if (!connected)
            {
                _hrBpm = -1;
                _hrHistory.Clear();
            }
        }
        finally
        {
            Monitor.Exit(_lock);
        }
    }

    /// <summary>清空心率显示数据。</summary>
    public void ClearHeartRate()
    {
        if (!Monitor.TryEnter(_lock, 2000))
        {
            _logger.LogWarning("覆盖层数据锁获取超时，跳过心率数据清空");
            return;
        }
        try
        {
            _hrBpm = -1;
            _hrHistory.Clear();
        }
        finally
        {
            Monitor.Exit(_lock);
        }
    }

    /// <summary>获取当前显示器列表（DeviceName + 是否主屏），供设置页屏幕下拉使用。</summary>
    public IReadOnlyList<(string DeviceName, bool IsPrimary)> GetScreenList()
        => EnumerateScreens().ConvertAll(s => (s.DeviceName, s.IsPrimary));

    /// <summary>心率覆盖层是否需要保持渲染（启用即显示；开启"未连接时隐藏"则需已连接）。</summary>
    private bool HeartRateActive()
    {
        if (Monitor.TryEnter(_lock, 2000))
        {
            try { return _hrEnabled && (!_hrHideWhenDisconnected || _hrConnected); }
            finally { Monitor.Exit(_lock); }
        }
        return false;   // 锁被异常持有：跳过本帧判定
    }

    /// <summary>判断指定覆盖层窗口是否为心率显示目标屏（匹配不到目标屏时回退主屏）。</summary>
    private bool IsHeartRateTarget(ScreenOverlay o)
    {
        if (o.IsSpan) return false;
        string target;
        if (!Monitor.TryEnter(_lock, 2000))
        {
            return false;   // 锁被异常持有：本帧不绘制心率
        }
        try
        {
            if (!_hrEnabled || (_hrHideWhenDisconnected && !_hrConnected)) return false;
            target = _hrTargetScreen;
        }
        finally
        {
            Monitor.Exit(_lock);
        }
        if (target != "PRIMARY")
        {
            var match = _overlays.Find(x => !x.IsSpan && x.DeviceName == target);
            if (match != null) return ReferenceEquals(o, match);
        }
        return o.IsPrimary;
    }

    /// <summary>从已保存设置初始化心率覆盖层配置（Start 时调用）。</summary>
    private void LoadInitialHeartRateConfig()
    {
        var s = _settings;
        SetHeartRateConfig(
            s.HeartRateOverlayEnabled,
            s.HeartRateStyle,
            s.HeartRateTargetScreen,
            s.HeartRateXPercent,
            s.HeartRateYPercent,
            s.HeartRateColor,
            s.HeartRateTextOutlineWidth,
            s.HeartRateScale,
            s.HeartRateAlertEnabled,
            s.HeartRateLowAlert,
            s.HeartRateHighAlert,
            s.HeartRateSpikeDelta,
            s.HeartRateHideWhenDisconnected);
    }

    /// <summary>绘制自由浮动心率元素（文本 / 胶囊卡片 / 心形+曲线，可组合）。</summary>
    private void DrawHeartRate(ScreenOverlay o)
    {
        var rt = o.RenderTarget;
        if (rt == null) return;

        int bpm;
        bool connected;
        int flags;
        float xPct, yPct, outlineW, scale;
        Color4 textColor, strokeColor;
        int[] history;
        bool alert;
        if (!Monitor.TryEnter(_lock, 2000))
        {
            return;   // 渲染线程持锁异常时跳过本帧心率绘制
        }
        try
        {
            if (!_hrEnabled || (_hrHideWhenDisconnected && !_hrConnected)) return;
            bpm = _hrBpm;
            connected = _hrConnected;
            flags = _hrStyleFlags;
            xPct = _hrXPct;
            yPct = _hrYPct;
            textColor = new Color4(_hrColorR / 255f, _hrColorG / 255f, _hrColorB / 255f, 1f);
            // 描边色为文本色反色
            strokeColor = new Color4((255 - _hrColorR) / 255f, (255 - _hrColorG) / 255f, (255 - _hrColorB) / 255f, 1f);
            outlineW = _hrOutlineWidth;
            scale = _hrScale;
            // 异常加速判定：开启且已连接且有数据
            alert = _hrAlertEnabled && connected && bpm > 0;
            if (alert)
            {
                if (bpm < _hrLowAlert || bpm > _hrHighAlert)
                    alert = true;
                else if (_hrHistory.Count >= 5)
                {
                    int n = Math.Min(_hrHistory.Count, 10);
                    int sum = 0;
                    for (int i = _hrHistory.Count - n; i < _hrHistory.Count; i++) sum += _hrHistory[i];
                    if (bpm - sum / n >= _hrSpikeDelta) alert = true;
                    else alert = false;
                }
                else alert = false;
            }
            history = [.. _hrHistory];
        }
        finally
        {
            Monitor.Exit(_lock);
        }

        bool showText = (flags & 1) != 0;
        bool showCard = (flags & 2) != 0;
        bool showHeart = (flags & 4) != 0;
        if (!showText && !showCard && !showHeart) showText = true;

        string bpmText = connected && bpm > 0 ? bpm.ToString() : "--";
        const float opacity = 0.92f;

        // 文本行（文本或卡片样式使用）
        string line = connected && bpm > 0 ? $"\u2764 {bpm} BPM" : "\u2764 -- 未连接";

        // 尺寸估算
        double nowSec = Environment.TickCount64 / 1000.0;
        float beatScale = ComputeBeatScale(nowSec, alert);

        float effOutline = outlineW * scale;   // 整体缩放后实际描边宽度
        float heartSize = 110f * scale;
        float lineFontSize = 18f * scale;
        float lineHeight = 26f * scale;
        float cardPadX = 14f * scale;
        float cardPadY = 6f * scale;

        float textLineWidth = 0f;
        IDWriteTextLayout? lineLayout = null;
        if (showText || showCard)
        {
            using var fmt = CreateTextFormat("Microsoft YaHei", DWriteFontWeight.SemiBold, lineFontSize);
            lineLayout = _dwFactory.CreateTextLayout(line, fmt, 10000, lineHeight);
            lineLayout.WordWrapping = WordWrapping.NoWrap;
            textLineWidth = lineLayout.Metrics.WidthIncludingTrailingWhitespace;
        }

        try
        {
            float blockW = 0f, blockH = 0f;
            float cardW = textLineWidth + cardPadX * 2;
            float cardH = lineHeight + cardPadY * 2;
            if (showHeart) { blockW = Math.Max(blockW, heartSize); blockH += heartSize; }
            if (showText || showCard)
            {
                blockW = Math.Max(blockW, showCard ? cardW : textLineWidth);
                if (showHeart) blockH += 6f * scale;
                blockH += showCard ? cardH : lineHeight;
            }

            // 按百分比定位（元素中心），并夹取到屏幕内
            float cxAnchor = o.Width * xPct / 100f;
            float cyAnchor = o.Height * yPct / 100f;
            float left = Math.Clamp(cxAnchor - blockW / 2f, 0, Math.Max(0, o.Width - blockW));
            float top = Math.Clamp(cyAnchor - blockH / 2f, 0, Math.Max(0, o.Height - blockH));

            float cursorY = top;

            if (showHeart)
            {
                float heartCenterX = left + blockW / 2f;
                float heartCenterY = cursorY + heartSize / 2f;
                DrawHeartShape(rt, heartCenterX, heartCenterY, heartSize * beatScale, opacity, bpmText, history);
                cursorY += heartSize + 6f * scale;
            }

            if (showText || showCard)
            {
                float rowW = showCard ? cardW : textLineWidth;
                float rowX = left + (blockW - rowW) / 2f;
                if (showCard)
                {
                    DrawPillBackground(rt, rowX, cursorY, cardW, cardH, cardH / 2f, opacity);
                }
                float textX = showCard ? rowX + cardPadX : rowX;
                float textY = showCard ? cursorY + cardPadY : cursorY;
                // 简洁文本描边（参考弹幕描边：8 方向偏移绘制，外圈 + 内半圈减少间隙，覆盖 0~outlineW）
                if (showText && effOutline > 0.05f && lineLayout != null)
                {
                    using var strokeBrush = CreateSolidColorBrush(rt, new Color4(strokeColor.R, strokeColor.G, strokeColor.B, opacity));
                    float[] radii = { effOutline, effOutline * 0.5f };
                    foreach (var r in radii)
                    {
                        if (r < 0.05f) continue;
                        for (int dx = -1; dx <= 1; dx++)
                            for (int dy = -1; dy <= 1; dy++)
                            {
                                if (dx == 0 && dy == 0) continue;
                                rt.DrawTextLayout(new Vector2(textX + dx * r, textY + dy * r), lineLayout, strokeBrush);
                            }
                    }
                }
                using var brush = CreateSolidColorBrush(rt, new Color4(textColor.R, textColor.G, textColor.B, opacity));
                if (lineLayout != null)
                    rt.DrawTextLayout(new Vector2(textX, textY), lineLayout, brush);
            }
        }
        finally
        {
            lineLayout?.Dispose();
        }
    }

    /// <summary>绘制心形（红色填充）+ 居中 BPM 数字 + 底部迷你心率曲线。centerX/centerY 为心形中心，缩放时围绕中心。</summary>
    private void DrawHeartShape(ID2D1DCRenderTarget rt, float centerX, float centerY, float size, float opacity, string bpmText, int[] history)
    {
        EnsureHeartGeometry();
        if (_hrHeartGeometry == null) return;

        // 由中心推导左上角，使 size 变化时心形围绕中心缩放而非左上角
        float x = centerX - size / 2f;
        float y = centerY - size / 2f;

        // 心形填充（单位几何缩放平移）
        var oldTransform = rt.Transform;
        rt.Transform = Matrix3x2.CreateScale(size, size) * Matrix3x2.CreateTranslation(x, y);
        using (var heartBrush = CreateSolidColorBrush(rt, new Color4(0.906f, 0.282f, 0.231f, opacity))) // #E7483B
        {
            rt.FillGeometry(_hrHeartGeometry, heartBrush);
        }
        rt.Transform = oldTransform;

        // BPM 数字居中（心形视觉中心约在 40% 高度处）
        using var numFmt = CreateTextFormat("Segoe UI", DWriteFontWeight.Bold, size * 0.26f);
        using var numLayout = _dwFactory.CreateTextLayout(bpmText, numFmt, size, size * 0.4f);
        numLayout.TextAlignment = DWriteTextAlignment.Center;
        using var numBrush = CreateSolidColorBrush(rt, new Color4(1, 1, 1, opacity));
        rt.DrawTextLayout(new Vector2(x, y + size * 0.24f), numLayout, numBrush);

        // 迷你心率曲线（心形中下部，55%~72% 高度带内）
        if (history.Length >= 2)
        {
            int min = int.MaxValue, max = int.MinValue;
            foreach (var v in history)
            {
                if (v < min) min = v;
                if (v > max) max = v;
            }
            int range = Math.Max(max - min, 10); // 避免平线时除零/过度放大

            float bandLeft = x + size * 0.28f;
            float bandWidth = size * 0.44f;
            float bandTop = y + size * 0.55f;
            float bandHeight = size * 0.17f;

            using var lineBrush = CreateSolidColorBrush(rt, new Color4(1, 1, 1, opacity * 0.85f));
            float stepX = bandWidth / (history.Length - 1);
            for (int i = 1; i < history.Length; i++)
            {
                float x0 = bandLeft + (i - 1) * stepX;
                float x1 = bandLeft + i * stepX;
                float y0 = bandTop + bandHeight * (1f - (history[i - 1] - min) / (float)range);
                float y1 = bandTop + bandHeight * (1f - (history[i] - min) / (float)range);
                rt.DrawLine(new Vector2(x0, y0), new Vector2(x1, y1), lineBrush, Math.Max(1f, size * 0.014f));
            }
        }
    }

    /// <summary>构建单位空间（0..1）心形路径几何并缓存。</summary>
    private void EnsureHeartGeometry()
    {
        if (_hrHeartGeometry != null) return;
        var geometry = _d2dFactory.CreatePathGeometry();
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
        _hrHeartGeometry = geometry;
    }

    private void DisposeHeartGeometry()
    {
        _hrHeartGeometry?.Dispose();
        _hrHeartGeometry = null;
    }

    /// <summary>计算心形跳动缩放：正常时轻微脉动；异常时突然加快且幅度更大（不跟随真实心率，避免性能开销）。</summary>
    private static float ComputeBeatScale(double tSec, bool alert)
    {
        const double TAU = Math.PI * 2.0;
        if (alert)
        {
            double phase = (tSec / 0.4) * TAU; // 周期约 0.4s，明显加快
            return 1f + 0.11f * (float)(0.5 - 0.5 * Math.Cos(phase)); // 0.89~1.11
        }
        double phase2 = (tSec / 1.1) * TAU;   // 周期约 1.1s，轻微
        return 1f + 0.04f * (float)(0.5 - 0.5 * Math.Cos(phase2));   // 0.96~1.04
    }
}
