using System.Numerics;
using Vortice.Direct2D1;
using Vortice.DirectWrite;
using Vortice.Mathematics;
using DWriteFontWeight = Vortice.DirectWrite.FontWeight;
using DWriteTextAlignment = Vortice.DirectWrite.TextAlignment;

namespace NotifyRelay.Services.Overlay.UI;

/// <summary>
/// 文本布局缓存：把 <see cref="IDWriteTextLayout"/> 与 <see cref="IDWriteTextFormat"/> 的
/// 创建参数一并记录，参数变化时才重建 —— 消除现有实现每帧新建 format/layout 的开销。
/// 由工厂创建，与渲染目标无关，可安全存放在节点槽位中跨帧复用。
/// </summary>
internal sealed class TextLayoutCache : IDisposable
{
    private IDWriteTextFormat? _format;
    private IDWriteTextLayout? _layout;
    private IDWriteTextLayout? _measureLayout;

    // 当前布局对应的参数
    private string? _text;
    private string _fontFamily = string.Empty;
    private DWriteFontWeight _weight;
    private float _size;
    private float _maxWidth;
    private float _maxHeight;
    private bool _truncate;

    public IDWriteTextLayout? Layout => _layout;
    public IDWriteTextFormat? Format => _format;

    /// <summary>当前布局的实测宽度（含尾随空白）。</summary>
    public float TextWidth => _layout?.Metrics.WidthIncludingTrailingWhitespace ?? 0f;

    /// <summary>当前布局的实测高度（真实换行高度，替代 size+6 的伪测量）。</summary>
    public float TextHeight => _layout?.Metrics.Height ?? 0f;

    /// <summary>测量不受 maxWidth 限制时的完整文本宽度（用于跑马灯溢出判定）。</summary>
    public float UnconstrainedWidth
    {
        get
        {
            if (_measureLayout == null) return TextWidth;
            return _measureLayout.Metrics.WidthIncludingTrailingWhitespace;
        }
    }

    public bool NeedsRebuild(string text, string fontFamily, DWriteFontWeight weight, float size,
        float maxWidth, float maxHeight, bool truncate)
        => _layout == null
           || !string.Equals(_text, text, StringComparison.Ordinal)
           || !string.Equals(_fontFamily, fontFamily, StringComparison.Ordinal)
           || _weight != weight
           || MathF.Abs(_size - size) > 1e-4f
           || MathF.Abs(_maxWidth - maxWidth) > 0.5f
           || MathF.Abs(_maxHeight - maxHeight) > 0.5f
           || _truncate != truncate;

    /// <summary>按需重建布局（参数未变则复用）。</summary>
    public void Ensure(MeasureScope s, string text, string fontFamily, DWriteFontWeight weight, float size,
        float maxWidth, float maxHeight, bool truncate)
    {
        if (!NeedsRebuild(text, fontFamily, weight, size, maxWidth, maxHeight, truncate)) return;

        _layout?.Dispose();
        _layout = null;
        _measureLayout?.Dispose();
        _measureLayout = null;

        if (MathF.Abs(_size - size) > 1e-4f || _format == null || _weight != weight
            || !string.Equals(_fontFamily, fontFamily, StringComparison.Ordinal))
        {
            _format?.Dispose();
            _format = s.CreateTextFormat(fontFamily, weight, size);
        }

        _layout = s.DwFactory.CreateTextLayout(text, _format,
            MathF.Max(1f, maxWidth), MathF.Max(1f, maxHeight));
        _layout.WordWrapping = WordWrapping.NoWrap;
        if (truncate)
        {
            using var ellipsis = s.DwFactory.CreateEllipsisTrimmingSign(_format);
            _layout.SetTrimming(
                new Trimming { Granularity = TrimmingGranularity.Character, Delimiter = 0, DelimiterCount = 0 },
                ellipsis);
        }

        _measureLayout = s.CreateMeasureLayout(text, _format);

        _text = text;
        _fontFamily = fontFamily;
        _weight = weight;
        _size = size;
        _maxWidth = maxWidth;
        _maxHeight = maxHeight;
        _truncate = truncate;
    }

    public void Dispose()
    {
        _layout?.Dispose(); _layout = null;
        _measureLayout?.Dispose(); _measureLayout = null;
        _format?.Dispose(); _format = null;
        _text = null;
    }
}

/// <summary>
/// 单行文本叶子。
/// 支持字符级省略号截断、8 方向反色描边（收敛时钟 / 心率各自手写的描边循环）。
/// </summary>
internal sealed class Text : OverlayNode
{
    public string TextValue = string.Empty;
    public string FontFamily = "Microsoft YaHei";
    public DWriteFontWeight Weight = DWriteFontWeight.Normal;
    public float FontSize = 12f;
    /// <summary>行高（0 = 使用布局实测高度）。</summary>
    public float LineHeight;
    /// <summary>
    /// 上报给父容器的行高（0 = 使用 <see cref="LineHeight"/>）。
    /// 与 <see cref="LineHeight"/> 分开是为了复刻现状：布局盒给足 1.4 倍避免裁切，
    /// 而卡片行高按字号计算（如罗技卡片 rowHeight = max(iconSize, textSize) + 2*paddingY）。
    /// </summary>
    public float ReportedHeight;
    /// <summary>可用宽度（0 = 不限制）。</summary>
    public float MaxWidth;
    /// <summary>是否以省略号截断（false 时超出直接裁掉）。</summary>
    public bool Ellipsis = true;
    public Color4 Color = new(1f, 1f, 1f, 1f);
    /// <summary>可选的颜色覆盖（如 HTML 分段着色），null 表示使用 <see cref="Color"/>。</summary>
    public Color4? ColorOverride;
    /// <summary>描边宽度（0 = 不描边）。</summary>
    public float OutlineWidth;
    /// <summary>描边颜色（一般为文本色反色）。</summary>
    public Color4 OutlineColor = new(0f, 0f, 0f, 1f);
    public DWriteTextAlignment Alignment = DWriteTextAlignment.Leading;
    /// <summary>整体不透明度（在 PaintScope 累积不透明度之外额外乘算）。</summary>
    public float Opacity = 1f;

    /// <summary>本节点使用的布局缓存（供跑马灯等需要完整文本宽度的场景读取）。</summary>
    internal TextLayoutCache Cache { get; private set; } = new();

    protected override Size Measure(MeasureScope s, Constraints c)
    {
        float maxW = MaxWidth > 0f ? MathF.Min(MaxWidth, c.MaxWidth) : c.MaxWidth;
        if (float.IsPositiveInfinity(maxW)) maxW = 10000f;
        float lineH = LineHeight > 0f ? LineHeight : FontSize * 1.4f;

        Cache.Ensure(s, TextValue, FontFamily, Weight, FontSize, maxW, lineH, Ellipsis);

        float w = MathF.Min(Cache.TextWidth, maxW);
        float h = ReportedHeight > 0f ? ReportedHeight : lineH;
        return c.Constrain(new Size(w, h));
    }

    protected override void Place(Rect rect) { }

    protected override void Paint(PaintScope s)
    {
        var layout = Cache.Layout;
        if (layout == null || string.IsNullOrEmpty(TextValue)) return;
        if (layout.TextAlignment != Alignment) layout.TextAlignment = Alignment;

        var b = Bounds;
        var color = ColorOverride ?? Color;
        float alpha = color.A * Opacity;

        if (OutlineWidth > 0.05f)
        {
            var stroke = s.BrushWithOpacity(new Color4(OutlineColor.R, OutlineColor.G, OutlineColor.B, OutlineColor.A * Opacity));
            float[] radii = { OutlineWidth, OutlineWidth * 0.5f };
            foreach (var r in radii)
            {
                if (r < 0.05f) continue;
                for (int dx = -1; dx <= 1; dx++)
                    for (int dy = -1; dy <= 1; dy++)
                    {
                        if (dx == 0 && dy == 0) continue;
                        s.Rt.DrawTextLayout(new Vector2(b.X + dx * r, b.Y + dy * r), layout, stroke);
                    }
            }
        }

        var brush = s.BrushWithOpacity(new Color4(color.R, color.G, color.B, alpha));
        s.Rt.DrawTextLayout(new Vector2(b.X, b.Y), layout, brush);
    }

    protected override void OnDispose() => Cache.Dispose();
}

/// <summary>HTML 颜色分段文本：支持 &lt;font color&gt; 分段着色，超宽回退整行截断。</summary>
internal sealed class RichText : OverlayNode
{
    public string Html = string.Empty;
    public string FontFamily = "Microsoft YaHei";
    public DWriteFontWeight Weight = DWriteFontWeight.Normal;
    public float FontSize = 12f;
    public float LineHeight;
    public float MaxWidth;
    public Color4 BaseColor = new(1f, 1f, 1f, 1f);
    public float Opacity = 1f;

    private readonly List<Segment> _segments = new(2);
    private bool _segmented;
    private float _totalWidth;
    private string? _lastHtml;
    private float _lastMaxWidth = -1f;
    private float _lastLineHeight = -1f;
    private string? _lastFontFamily;
    private DWriteFontWeight _lastWeight;
    private float _lastFontSize = -1f;
    private IDWriteTextFormat? _format;
    private string? _formatFontFamily;
    private DWriteFontWeight _formatWeight;
    private float _formatSize = -1f;
    private TextLayoutCache? _wholeCache;

    private sealed class Segment
    {
        public Color4? Color;
        public IDWriteTextLayout? Layout;
    }

    protected override Size Measure(MeasureScope s, Constraints c)
    {
        float maxW = MaxWidth > 0f ? MathF.Min(MaxWidth, c.MaxWidth) : c.MaxWidth;
        if (float.IsPositiveInfinity(maxW)) maxW = 10000f;
        float lineH = LineHeight > 0f ? LineHeight : FontSize * 1.4f;

        EnsureFormat(s);
        RebuildIfNeeded(s, maxW, lineH);

        float w;
        if (_segmented)
        {
            w = _totalWidth;
        }
        else
        {
            _wholeCache ??= new TextLayoutCache();
            _wholeCache.Ensure(s, HtmlColorParser.Unescape(Html), FontFamily, Weight, FontSize,
                maxW, lineH, true);
            w = _wholeCache.TextWidth;
        }
        return c.Constrain(new Size(MathF.Min(w, maxW), lineH));
    }

    private void EnsureFormat(MeasureScope s)
    {
        // 字族 / 字重 / 字号变化时重建（同一节点通常固定，但需保证参数变更后不沿用旧格式）
        if (_format != null
            && string.Equals(_formatFontFamily, FontFamily, StringComparison.Ordinal)
            && _formatWeight == Weight
            && MathF.Abs(_formatSize - FontSize) <= 1e-4f)
        {
            return;
        }

        _format?.Dispose();
        _format = s.CreateTextFormat(FontFamily, Weight, FontSize);
        _formatFontFamily = FontFamily;
        _formatWeight = Weight;
        _formatSize = FontSize;
    }

    /// <summary>段落化：仅当分段总宽不超过可用宽度时才分段着色（与现有实现判定一致）。</summary>
    private void RebuildIfNeeded(MeasureScope s, float maxW, float lineH)
    {
        if (string.Equals(_lastHtml, Html, StringComparison.Ordinal)
            && MathF.Abs(_lastMaxWidth - maxW) < 0.5f
            && MathF.Abs(_lastLineHeight - lineH) < 0.5f
            && string.Equals(_lastFontFamily, FontFamily, StringComparison.Ordinal)
            && _lastWeight == Weight
            && MathF.Abs(_lastFontSize - FontSize) < 1e-4f)
        {
            return;
        }

        _lastHtml = Html;
        _lastMaxWidth = maxW;
        _lastLineHeight = lineH;
        _lastFontFamily = FontFamily;
        _lastWeight = Weight;
        _lastFontSize = FontSize;

        DisposeSegments();
        _segmented = false;
        _totalWidth = 0f;

        var parsed = HtmlColorParser.Parse(Html);
        if (parsed.Count <= 1) return;

        float total = 0f;
        foreach (var (text, _) in parsed)
            total += s.MeasureTextWidth(text, FontFamily, Weight, FontSize);

        if (total > maxW) return;

        foreach (var (text, color) in parsed)
        {
            var layout = s.DwFactory.CreateTextLayout(text, _format!, 10000f, lineH);
            layout.WordWrapping = WordWrapping.NoWrap;
            _segments.Add(new Segment { Color = color, Layout = layout });
        }
        _totalWidth = total;
        _segmented = true;
    }

    protected override void Place(Rect rect) { }

    protected override void Paint(PaintScope s)
    {
        var b = Bounds;
        var baseBrush = s.BrushWithOpacity(new Color4(BaseColor.R, BaseColor.G, BaseColor.B, BaseColor.A * Opacity));

        if (_segmented && _segments.Count > 0)
        {
            float x = b.X;
            foreach (var seg in _segments)
            {
                if (seg.Layout == null) continue;
                var brush = seg.Color is { } c
                    ? s.BrushWithOpacity(new Color4(c.R, c.G, c.B, c.A * Opacity))
                    : baseBrush;
                s.Rt.DrawTextLayout(new Vector2(x, b.Y), seg.Layout, brush);
                x += seg.Layout.Metrics.WidthIncludingTrailingWhitespace;
            }
            return;
        }

        var whole = _wholeCache?.Layout;
        if (whole != null)
        {
            s.Rt.DrawTextLayout(new Vector2(b.X, b.Y), whole, baseBrush);
            return;
        }

        using var format = s.CreateTextFormat(FontFamily, Weight, FontSize);
        using var layout = s.CreateTruncatedLayout(HtmlColorParser.Unescape(Html), format,
            MathF.Max(1f, b.Width), LineHeight > 0f ? LineHeight : FontSize * 1.4f);
        s.Rt.DrawTextLayout(new Vector2(b.X, b.Y), layout, baseBrush);
    }

    private void DisposeSegments()
    {
        foreach (var seg in _segments)
        {
            try { seg.Layout?.Dispose(); } catch { }
        }
        _segments.Clear();
    }

    protected override void OnDispose()
    {
        DisposeSegments();
        _wholeCache?.Dispose();
        _wholeCache = null;
        _format?.Dispose();
        _format = null;
    }
}

/// <summary>
/// 图标 / 字形文本叶子（Segoe MDL2 Assets 字形、Emoji 音符等）。
/// 与 <see cref="Text"/> 的区别：不截断、按字符尺寸量测，可直接用于图标槽位。
/// </summary>
internal sealed class Icon : OverlayNode
{
    public string Glyph = string.Empty;
    public string FontFamily = "Segoe MDL2 Assets";
    public DWriteFontWeight Weight = DWriteFontWeight.Normal;
    public float FontSize = 20f;
    /// <summary>上报给父容器的宽度（0 = 上报 0，即不参与宽度累加，用于「图标盒 fixedSize + 名称 textW」的固定卡片宽模型）。</summary>
    public float ReportedWidth;
    public Color4 Color = new(1f, 1f, 1f, 1f);
    public float Opacity = 1f;
    /// <summary>为 true 时按 0.5 倍不透明度绘制（对齐现有音符图标）。</summary>
    public bool HalfOpacity;

    private TextLayoutCache? _cache;

    protected override Size Measure(MeasureScope s, Constraints c)
    {
        _cache ??= new TextLayoutCache();
        float h = FontSize * 1.4f;
        _cache.Ensure(s, Glyph, FontFamily, Weight, FontSize, FontSize * 3f, h, false);
        return c.Constrain(new Size(ReportedWidth, h));
    }

    protected override void Place(Rect rect) { }

    protected override void Paint(PaintScope s)
    {
        var layout = _cache?.Layout;
        if (layout == null || string.IsNullOrEmpty(Glyph)) return;
        float a = Color.A * Opacity * (HalfOpacity ? 0.5f : 1f);
        var brush = s.BrushWithOpacity(new Color4(Color.R, Color.G, Color.B, a));
        s.Rt.DrawTextLayout(new Vector2(Bounds.X, Bounds.Y), layout, brush);
    }

    protected override void OnDispose()
    {
        _cache?.Dispose();
        _cache = null;
    }
}

/// <summary>
/// 位图叶子：把 <see cref="ID2D1Bitmap"/> 缩放到（或装入）给定尺寸。
/// 位图由 Resources 懒加载并挂在 item 上，节点只引用不持有（rt 失效由现有钩子处理）。
/// </summary>
internal sealed class Bitmap : OverlayNode
{
    /// <summary>位图提供者：每次 Measure/Paint 读取（item 的位图槽可能被 rt 变化置空后重建）。</summary>
    public Func<ID2D1Bitmap?>? Source;
    /// <summary>绘制边长（正方形）。</summary>
    public float DrawSize;
    /// <summary>绘制不透明度。</summary>
    public float Opacity = 1f;
    /// <summary>位图为空且本标志为 true 时绘制灰色圆形占位（对齐 DrawCirclePlaceholder）。</summary>
    public bool PlaceholderWhenEmpty;

    protected override Size Measure(MeasureScope s, Constraints c)
        => c.Constrain(new Size(DrawSize, DrawSize));

    protected override void Place(Rect rect) { }

    protected override void Paint(PaintScope s)
    {
        var bmp = Source?.Invoke();
        var b = Bounds;
        if (bmp != null)
        {
            DrawScaled(s, bmp, b.X, b.Y, DrawSize, Opacity * s.Opacity);
            return;
        }
        if (PlaceholderWhenEmpty)
        {
            var brush = s.BrushWithOpacity(new Color4(0.5f, 0.5f, 0.5f, 0.5f * Opacity));
            var center = new Vector2(b.X + DrawSize / 2f, b.Y + DrawSize / 2f);
            s.Rt.FillEllipse(new Ellipse(center, DrawSize / 2f, DrawSize / 2f), brush);
        }
    }

    /// <summary>按边长等比缩放绘制（等价旧 DrawCoverBitmap：以左上角为基准等比缩放）。</summary>
    public static void DrawScaled(PaintScope s, ID2D1Bitmap bitmap, float x, float y, float size, float opacity)
    {
        var bmpSize = bitmap.Size;
        float scale = size / MathF.Max(1f, MathF.Max(bmpSize.Width, bmpSize.Height));
        s.PushTransform(Matrix3x2.CreateScale(scale, scale) * Matrix3x2.CreateTranslation(x, y));
        try
        {
            s.Rt.DrawBitmap(bitmap, opacity, Vortice.Direct2D1.BitmapInterpolationMode.Linear);
        }
        finally
        {
            s.PopTransform();
        }
    }
}

/// <summary>
/// 逃生画布节点：把难以用通用节点描述的层叠 / 绝对定位 / 条件显隐内容
/// 原样交给直接 D2D 绘制（如多节点进度条、心形+曲线、媒体频谱）。
/// 尺寸由 <see cref="MeasureSize"/> 给定，绘制在 <see cref="OnPaint"/> 内自行定位。
/// </summary>
internal sealed class Canvas : OverlayNode
{
    /// <summary>尺寸提供者（可为 null，表示通过 <see cref="FixedSize"/> 给定）。</summary>
    public Func<Size>? MeasureSize;
    public Size FixedSize;
    /// <summary>为 true 时上报 0 尺寸（作为 Stack 内的背景层，实际范围取 Place 得到的矩形）。</summary>
    public bool IgnoreInMeasure;
    /// <summary>绘制回调：参数为绘制上下文与本节点矩形。</summary>
    public Action<PaintScope, Rect>? OnPaint;

    protected override Size Measure(MeasureScope s, Constraints c)
    {
        if (IgnoreInMeasure) return Size.Zero;
        var size = MeasureSize?.Invoke() ?? FixedSize;
        return c.Constrain(size);
    }

    protected override void Place(Rect rect) { }

    protected override void Paint(PaintScope s) => OnPaint?.Invoke(s, Bounds);

    protected override void OnDispose() => OnPaint = null;
}

/// <summary>HTML 颜色标签解析（从 Rendering.cs 的 ParseHtmlColorSegments 上提，供 RichText 复用）。</summary>
internal static class HtmlColorParser
{
    /// <summary>还原 JSON 转义序列与 HTML 实体（对齐 Android unescapeHtml）。</summary>
    public static string Unescape(string html)
        => html.Replace("\\u003c", "<").Replace("\\u003e", ">")
            .Replace("\\u0027", "'").Replace("\\u0022", "\"")
            .Replace("\\u0026", "&")
            .Replace("&lt;", "<").Replace("&gt;", ">")
            .Replace("&quot;", "\"").Replace("&apos;", "'").Replace("&amp;", "&");

    /// <summary>
    /// 解析简单 HTML 颜色标签为分段文本：仅支持 &lt;font color='#RRGGBB'&gt;（单/双引号、3/6 位十六进制），
    /// 其余标签剥掉保留文本。
    /// </summary>
    public static List<(string Text, Color4? Color)> Parse(string html)
    {
        var source = Unescape(html);
        var result = new List<(string, Color4?)>();
        var stack = new List<Color4?>();
        var sb = new System.Text.StringBuilder();

        void Flush()
        {
            if (sb.Length == 0) return;
            result.Add((sb.ToString(), stack.Count > 0 ? stack[^1] : null));
            sb.Clear();
        }

        int pos = 0;
        while (pos < source.Length)
        {
            int lt = source.IndexOf('<', pos);
            if (lt < 0) { sb.Append(source, pos, source.Length - pos); break; }
            if (lt > pos) sb.Append(source, pos, lt - pos);
            int gt = source.IndexOf('>', lt);
            if (gt < 0) { sb.Append(source, lt, source.Length - lt); break; }

            var tag = source.Substring(lt + 1, gt - lt - 1).Trim();
            if (tag.StartsWith("/font", StringComparison.OrdinalIgnoreCase))
            {
                Flush();
                if (stack.Count > 0) stack.RemoveAt(stack.Count - 1);
            }
            else if (tag.StartsWith("font", StringComparison.OrdinalIgnoreCase))
            {
                Color4? spanColor = null;
                int ci = tag.IndexOf("color", StringComparison.OrdinalIgnoreCase);
                if (ci >= 0)
                {
                    int eq = tag.IndexOf('=', ci);
                    if (eq >= 0 && eq + 1 < tag.Length)
                    {
                        char q = tag[eq + 1];
                        if (q is '\'' or '"')
                        {
                            int end = tag.IndexOf(q, eq + 2);
                            if (end > eq + 1)
                                spanColor = ColorHexParser.Parse(tag.Substring(eq + 2, end - eq - 2));
                        }
                    }
                }
                Flush();
                stack.Add(spanColor);
            }
            pos = gt + 1;
        }
        Flush();
        return result;
    }
}

/// <summary>#RRGGBB 解析（从 Rendering.cs 的 ParseHexColor 上提，供节点与解析器共用）。</summary>
internal static class ColorHexParser
{
    /// <summary>解析 #RRGGBB（可省 #），失败返回 null。</summary>
    public static Color4? Parse(string? hex)
    {
        if (string.IsNullOrEmpty(hex)) return null;
        var s = hex.TrimStart('#');
        if (s.Length == 6
            && byte.TryParse(s.AsSpan(0, 2), System.Globalization.NumberStyles.HexNumber, null, out byte r)
            && byte.TryParse(s.AsSpan(2, 2), System.Globalization.NumberStyles.HexNumber, null, out byte g)
            && byte.TryParse(s.AsSpan(4, 2), System.Globalization.NumberStyles.HexNumber, null, out byte b))
        {
            return new Color4(r / 255f, g / 255f, b / 255f, 1f);
        }
        return null;
    }

    /// <summary>解析 #RRGGBB 中指定通道（offset 为 0/2/4），失败返回 fallback。</summary>
    public static byte ParseChannel(string? hex, byte fallback, int offset)
    {
        if (string.IsNullOrEmpty(hex) || !hex.StartsWith('#')) return fallback;
        var s = hex.TrimStart('#');
        if (s.Length != 6) return fallback;
        try
        {
            return byte.Parse(s.AsSpan(offset, 2), System.Globalization.NumberStyles.HexNumber);
        }
        catch (FormatException)
        {
            return fallback;
        }
    }

    /// <summary>浅色/深色双色字段取色：PC 深色背景优先取 Dark，缺失回退浅色。</summary>
    public static string? PreferDark(string? light, string? dark) => dark ?? light;

    /// <summary>解析双色字段（优先深色）为 Color4；均缺失时返回 fallback。</summary>
    public static Color4 ResolveTheme(string? light, string? dark, Color4 fallback)
        => Parse(dark) ?? Parse(light) ?? fallback;
}
