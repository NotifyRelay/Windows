using System.Globalization;
using Vortice.Mathematics;
using DWriteFontWeight = Vortice.DirectWrite.FontWeight;

namespace NotifyRelay.Services.Overlay.UI.Elements;

/// <summary>
/// 时间浮窗元素：自由浮动的时间文本（无背景，仅字体描边）。
/// 声明式形态：<c>Align(center, x%, y%) { Text(时间, 48*scale, outline) }</c>
/// </summary>
internal sealed class ClockElement : IOverlayElement
{
    // 布局常量（与旧实现一致）
    private const float BaseFontSize = 48f;
    private const float Opacity = 0.95f;

    private readonly ElementContext _ctx;

    // 状态（_lock 保护）
    private bool _enabled;
    private string _targetScreen = "PRIMARY";
    private float _xPct = 50f;
    private float _yPct = 10f;
    private byte _colorR = 255, _colorG = 255, _colorB = 255;
    private float _outlineWidth = 2f;
    private float _scale = 1f;
    private bool _showSeconds = true;
    private bool _use24Hour = true;

    // 渲染缓存（仅渲染线程访问）：时间文本变化时才重建布局与画刷
    private string? _cacheText;

    public ClockElement(ElementContext ctx) => _ctx = ctx;

    public string Name => "Clock";

    public void LoadSettings(IOverlaySettings s)
        => SetConfig(s.ClockOverlayEnabled, s.ClockTargetScreen, s.ClockXPercent, s.ClockYPercent,
            s.ClockColor, s.ClockTextOutlineWidth, s.ClockScale, s.ClockShowSeconds, s.ClockUse24Hour);

    /// <summary>更新时间浮窗配置（启用、目标屏、位置百分比、颜色、描边、缩放、格式）。</summary>
    public void SetConfig(bool enabled, string targetScreen, float xPct, float yPct,
        string colorHex, float outlineWidth, float scale, bool showSeconds, bool use24Hour)
    {
        _ctx.WithLock(() =>
        {
            _enabled = enabled;
            _targetScreen = string.IsNullOrEmpty(targetScreen) ? "PRIMARY" : targetScreen;
            _xPct = Math.Clamp(xPct, 0f, 100f);
            _yPct = Math.Clamp(yPct, 0f, 100f);
            _colorR = ColorHexParser.ParseChannel(colorHex, 255, 0);
            _colorG = ColorHexParser.ParseChannel(colorHex, 255, 2);
            _colorB = ColorHexParser.ParseChannel(colorHex, 255, 4);
            _outlineWidth = Math.Clamp(outlineWidth, 0.1f, 3f);
            _scale = Math.Clamp(scale, 0.5f, 2f);
            _showSeconds = showSeconds;
            _use24Hour = use24Hour;
        }, "覆盖层数据锁获取超时，跳过时间浮窗配置更新");
    }

    public bool IsActive() => _ctx.WithLock(() => _enabled, false);

    public bool IsTargetScreen(ScreenOverlay o)
    {
        if (o.IsSpan) return false;   // 时间浮窗不支持跨屏窗口
        var target = _ctx.WithLock<string?>(() => _enabled ? _targetScreen : null, null);
        if (target == null) return false;   // 未启用或锁被异常持有：本帧不绘制时间浮窗
        return _ctx.IsTargetScreen(o, target, allowSpan: false);
    }

    /// <summary>当前时间文本（按显示精度），供「仅秒变化时重绘」判定使用。</summary>
    public string GetTimeText()
    {
        bool showSeconds, use24Hour;
        lock (_ctx.StateLock)
        {
            showSeconds = _showSeconds;
            use24Hour = _use24Hour;
        }
        return DateTime.Now.ToString(ClockFormat(use24Hour, showSeconds), CultureInfo.CurrentCulture);
    }

    public void Compose(OverlayComposer composer, ScreenOverlay o)
    {
        float xPct, yPct, outlineW, scale;
        Color4 textColor, strokeColor;
        bool showSeconds, use24Hour;
        lock (_ctx.StateLock)
        {
            if (!_enabled) return;
            xPct = _xPct;
            yPct = _yPct;
            textColor = new Color4(_colorR / 255f, _colorG / 255f, _colorB / 255f, Opacity);
            // 描边色为文本色反色（与心率描边一致）
            strokeColor = new Color4((255 - _colorR) / 255f, (255 - _colorG) / 255f, (255 - _colorB) / 255f, Opacity);
            outlineW = _outlineWidth;
            scale = _scale;
            showSeconds = _showSeconds;
            use24Hour = _use24Hour;
        }

        string timeText = DateTime.Now.ToString(ClockFormat(use24Hour, showSeconds), CultureInfo.CurrentCulture);
        if (!string.Equals(_cacheText, timeText, StringComparison.Ordinal)) _cacheText = timeText;

        float fontSize = BaseFontSize * scale;
        float effOutline = outlineW * scale;

        composer.Node<Align>(null, a =>
        {
            a.XPct = xPct;
            a.YPct = yPct;
            a.AnchorAtCenterX = true; a.AnchorAtCenterY = true;
            a.ClampToBounds = true;
        }, () =>
        {
            composer.Leaf<Text>("time", t =>
            {
                t.TextValue = timeText;
                t.FontFamily = "Microsoft YaHei";
                t.Weight = DWriteFontWeight.Bold;
                t.FontSize = fontSize;
                t.LineHeight = fontSize * 1.4f;
                t.Color = textColor;
                t.OutlineWidth = effOutline;
                t.OutlineColor = strokeColor;
                t.Ellipsis = false;
            });
        });
    }

    /// <summary>释放时钟渲染缓存（窗口清理时调用）。</summary>
    public void Reset() => _cacheText = null;

    /// <summary>按显示精度（秒/分）返回时间格式串。</summary>
    private static string ClockFormat(bool use24Hour, bool showSeconds) => (use24Hour, showSeconds) switch
    {
        (true, true) => "HH:mm:ss",
        (true, false) => "HH:mm",
        (false, true) => "h:mm:ss tt",
        (false, false) => "h:mm tt"
    };
}
