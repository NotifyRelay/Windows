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
    private float _hrOutlineWidth = 2f;     // 简洁文本描边粗细（像素，0~6）
    private int _hrBpm = -1;                // -1 = 无数据
    private bool _hrConnected;
    private readonly List<int> _hrHistory = [];
    private const int HrHistoryMax = 60;

    // 心形几何缓存（设备无关资源，0..1 单位空间，绘制时缩放平移）
    private ID2D1PathGeometry? _hrHeartGeometry;

    /// <summary>更新心率覆盖层配置（启用、样式组合、目标屏、位置百分比、颜色）。</summary>
    public void SetHeartRateConfig(bool enabled, int styleFlags, string targetScreen, float xPct, float yPct, string colorHex, float outlineWidth)
    {
        lock (_lock)
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
            if (!enabled)
            {
                // 关闭显示时清空历史，避免下次开启残留旧曲线
                _hrHistory.Clear();
            }
        }
    }

    /// <summary>推送最新心率值（BLE 通知回调线程调用）。</summary>
    public void UpdateHeartRate(int bpm)
    {
        if (bpm <= 0) return;
        lock (_lock)
        {
            _hrBpm = bpm;
            _hrHistory.Add(bpm);
            if (_hrHistory.Count > HrHistoryMax)
                _hrHistory.RemoveAt(0);
        }
    }

    /// <summary>设置心率设备连接状态；断开时清空当前值与历史。</summary>
    public void SetHeartRateConnected(bool connected)
    {
        lock (_lock)
        {
            _hrConnected = connected;
            if (!connected)
            {
                _hrBpm = -1;
                _hrHistory.Clear();
            }
        }
    }

    /// <summary>清空心率显示数据。</summary>
    public void ClearHeartRate()
    {
        lock (_lock)
        {
            _hrBpm = -1;
            _hrHistory.Clear();
        }
    }

    /// <summary>获取当前显示器列表（DeviceName + 是否主屏），供设置页屏幕下拉使用。</summary>
    public IReadOnlyList<(string DeviceName, bool IsPrimary)> GetScreenList()
        => EnumerateScreens().ConvertAll(s => (s.DeviceName, s.IsPrimary));

    /// <summary>心率覆盖层是否需要保持渲染（启用即显示，未连接时显示占位）。</summary>
    private bool HeartRateActive()
    {
        lock (_lock) return _hrEnabled;
    }

    /// <summary>判断指定覆盖层窗口是否为心率显示目标屏（匹配不到目标屏时回退主屏）。</summary>
    private bool IsHeartRateTarget(ScreenOverlay o)
    {
        if (o.IsSpan) return false;
        string target;
        lock (_lock)
        {
            if (!_hrEnabled) return false;
            target = _hrTargetScreen;
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
            s.HeartRateTextOutlineWidth);
    }

    /// <summary>绘制自由浮动心率元素（文本 / 胶囊卡片 / 心形+曲线，可组合）。</summary>
    private void DrawHeartRate(ScreenOverlay o)
    {
        var rt = o.RenderTarget;
        if (rt == null) return;

        int bpm;
        bool connected;
        int flags;
        float xPct, yPct, outlineW;
        Color4 textColor, strokeColor;
        int[] history;
        lock (_lock)
        {
            if (!_hrEnabled) return;
            bpm = _hrBpm;
            connected = _hrConnected;
            flags = _hrStyleFlags;
            xPct = _hrXPct;
            yPct = _hrYPct;
            textColor = new Color4(_hrColorR / 255f, _hrColorG / 255f, _hrColorB / 255f, 1f);
            // 描边色为文本色反色
            strokeColor = new Color4((255 - _hrColorR) / 255f, (255 - _hrColorG) / 255f, (255 - _hrColorB) / 255f, 1f);
            outlineW = _hrOutlineWidth;
            history = [.. _hrHistory];
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
        const float heartSize = 110f;
        const float lineFontSize = 18f;
        const float lineHeight = 26f;
        const float cardPadX = 14f;
        const float cardPadY = 6f;

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
                if (showHeart) blockH += 6f;
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
                float heartX = left + (blockW - heartSize) / 2f;
                DrawHeartShape(rt, heartX, cursorY, heartSize, opacity, bpmText, history);
                cursorY += heartSize + 6f;
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
                if (showText && outlineW > 0.05f && lineLayout != null)
                {
                    using var strokeBrush = CreateSolidColorBrush(rt, new Color4(strokeColor.R, strokeColor.G, strokeColor.B, opacity));
                    float[] radii = { outlineW, outlineW * 0.5f };
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

    /// <summary>绘制心形（红色填充）+ 居中 BPM 数字 + 底部迷你心率曲线。</summary>
    private void DrawHeartShape(ID2D1DCRenderTarget rt, float x, float y, float size, float opacity, string bpmText, int[] history)
    {
        EnsureHeartGeometry();
        if (_hrHeartGeometry == null) return;

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
                rt.DrawLine(new Vector2(x0, y0), new Vector2(x1, y1), lineBrush, 1.5f);
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
}
