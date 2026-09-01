using System.Globalization;
using System.Numerics;
using Vortice.Direct2D1;
using Vortice.DirectWrite;
using Vortice.Mathematics;
using DWriteFontWeight = Vortice.DirectWrite.FontWeight;

namespace NotifyRelay.Services.Overlay;

public partial class OverlayRenderService
{
    // 时间浮窗状态（_lock 保护）
    private bool _clockEnabled;
    private string _clockTargetScreen = "PRIMARY";
    private float _clockXPct = 50f;
    private float _clockYPct = 10f;
    private byte _clockColorR = 255, _clockColorG = 255, _clockColorB = 255;
    private float _clockOutlineWidth = 2f;     // 文本描边粗细（像素，0.1~3）
    private float _clockScale = 1f;           // 整体显示大小缩放（0.5~2）
    private bool _clockShowSeconds = true;    // 是否显示秒
    private bool _clockUse24Hour = true;      // 是否使用 24 小时制

    /// <summary>更新时间浮窗配置（启用、目标屏、位置百分比、颜色、描边、缩放、格式）。</summary>
    public void SetClockConfig(bool enabled, string targetScreen, float xPct, float yPct,
        string colorHex, float outlineWidth, float scale, bool showSeconds, bool use24Hour)
    {
        if (!Monitor.TryEnter(_lock, 2000))
        {
            _logger.LogWarning("覆盖层数据锁获取超时，跳过时间浮窗配置更新");
            return;
        }
        try
        {
            _clockEnabled = enabled;
            _clockTargetScreen = string.IsNullOrEmpty(targetScreen) ? "PRIMARY" : targetScreen;
            _clockXPct = Math.Clamp(xPct, 0f, 100f);
            _clockYPct = Math.Clamp(yPct, 0f, 100f);
            _clockColorR = ParseColorChannel(colorHex, 255, 0);
            _clockColorG = ParseColorChannel(colorHex, 255, 2);
            _clockColorB = ParseColorChannel(colorHex, 255, 4);
            _clockOutlineWidth = Math.Clamp(outlineWidth, 0.1f, 3f);
            _clockScale = Math.Clamp(scale, 0.5f, 2f);
            _clockShowSeconds = showSeconds;
            _clockUse24Hour = use24Hour;
        }
        finally
        {
            Monitor.Exit(_lock);
        }
    }

    /// <summary>时间浮窗是否需要保持渲染（启用即显示）。</summary>
    private bool ClockActive()
    {
        if (Monitor.TryEnter(_lock, 2000))
        {
            try { return _clockEnabled; }
            finally { Monitor.Exit(_lock); }
        }
        return false;   // 锁被异常持有：跳过本帧判定
    }

    /// <summary>判断指定覆盖层窗口是否为时间浮窗目标屏（匹配不到目标屏时回退主屏）。</summary>
    private bool IsClockTarget(ScreenOverlay o)
    {
        if (o.IsSpan) return false;   // 时间浮窗不支持跨屏窗口
        string target;
        if (!Monitor.TryEnter(_lock, 2000))
        {
            return false;   // 锁被异常持有：本帧不绘制时间浮窗
        }
        try
        {
            if (!_clockEnabled) return false;
            target = _clockTargetScreen;
        }
        finally
        {
            Monitor.Exit(_lock);
        }
        // 目标屏解析复用共用核心（primary / 设备名精确匹配 / 回退主屏），
        // 与心率、罗技电池等元素共用同一真源，避免多处实现漂移
        return OverlayElementCore.IsTargetScreen(o, target,
            _windowManager.Overlays, _windowManager.SpanOverlay, allowSpan: false);
    }

    /// <summary>从已保存设置初始化时间浮窗配置（Start 时调用）。</summary>
    private void LoadInitialClockConfig()
    {
        var s = _settings;
        SetClockConfig(
            s.ClockOverlayEnabled,
            s.ClockTargetScreen,
            s.ClockXPercent,
            s.ClockYPercent,
            s.ClockColor,
            s.ClockTextOutlineWidth,
            s.ClockScale,
            s.ClockShowSeconds,
            s.ClockUse24Hour);
    }

    /// <summary>绘制自由浮动的时间文本（无背景，仅字体描边，参考心率描边）。</summary>
    private void DrawClock(ScreenOverlay o)
    {
        var rt = o.RenderTarget;
        if (rt == null) return;

        float xPct, yPct, outlineW, scale;
        Color4 textColor, strokeColor;
        bool showSeconds, use24Hour;
        if (!Monitor.TryEnter(_lock, 2000))
        {
            return;   // 渲染线程持锁异常时跳过本帧时间浮窗绘制
        }
        try
        {
            if (!_clockEnabled) return;
            xPct = _clockXPct;
            yPct = _clockYPct;
            textColor = new Color4(_clockColorR / 255f, _clockColorG / 255f, _clockColorB / 255f, 1f);
            // 描边色为文本色反色（与心率描边一致）
            strokeColor = new Color4((255 - _clockColorR) / 255f, (255 - _clockColorG) / 255f, (255 - _clockColorB) / 255f, 1f);
            outlineW = _clockOutlineWidth;
            scale = _clockScale;
            showSeconds = _clockShowSeconds;
            use24Hour = _clockUse24Hour;
        }
        finally
        {
            Monitor.Exit(_lock);
        }

        string format = (use24Hour, showSeconds) switch
        {
            (true, true) => "HH:mm:ss",
            (true, false) => "HH:mm",
            (false, true) => "h:mm:ss tt",
            (false, false) => "h:mm tt"
        };
        string timeText = DateTime.Now.ToString(format, CultureInfo.CurrentCulture);

        const float opacity = 0.95f;
        float fontSize = 48f * scale;
        float effOutline = outlineW * scale;   // 整体缩放后实际描边宽度

        using var fmt = CreateTextFormat("Microsoft YaHei", DWriteFontWeight.Bold, fontSize);
        using var layout = _dwFactory.CreateTextLayout(timeText, fmt, 10000, fontSize * 1.4f);
        layout.WordWrapping = WordWrapping.NoWrap;
        float textWidth = layout.Metrics.WidthIncludingTrailingWhitespace;
        float textHeight = layout.Metrics.Height;

        // 按百分比定位（元素中心），并夹取到屏幕内
        var (cxAnchor, cyAnchor) = OverlayElementCore.ResolveAnchor(o, xPct, yPct);
        float left = Math.Clamp(cxAnchor - textWidth / 2f, 0, Math.Max(0, o.Width - textWidth));
        float top = Math.Clamp(cyAnchor - textHeight / 2f, 0, Math.Max(0, o.Height - textHeight));

        // 仅描边，无背景：8 方向偏移绘制（外圈 + 内半圈减少间隙，覆盖 0~outlineW）
        if (effOutline > 0.05f)
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
                        rt.DrawTextLayout(new Vector2(left + dx * r, top + dy * r), layout, strokeBrush);
                    }
            }
        }

        // 主文本（前景）
        using var brush = CreateSolidColorBrush(rt, new Color4(textColor.R, textColor.G, textColor.B, opacity));
        rt.DrawTextLayout(new Vector2(left, top), layout, brush);
    }
}
