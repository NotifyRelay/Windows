using System.Numerics;
using System.Drawing;
using Vortice.Direct2D1;
using Vortice.Mathematics;
using DWriteFontWeight = Vortice.DirectWrite.FontWeight;
using D2DArcSegment = Vortice.Direct2D1.ArcSegment;

namespace NotifyRelay.Services.Overlay.UI;

/// <summary>圆角进度条（轨道 + 前景），收敛媒体卡片 / 默认模板 / 线性进度的重复绘制。</summary>
internal sealed class ProgressBar : OverlayNode
{
    /// <summary>轨道颜色（A=0 表示不画轨道）。</summary>
    public Color4 TrackColor = new(0f, 0f, 0f, 0f);
    public Color4 FillColor = new(0.3f, 0.7f, 1f, 1f);
    public float Height = 4f;
    public float Radius = 2f;
    /// <summary>进度百分比（0~100）。</summary>
    public float Progress;
    public float Opacity = 1f;

    protected override Size Measure(MeasureScope s, Constraints c)
        => c.Constrain(new Size(float.IsPositiveInfinity(c.MaxWidth) ? 0f : c.MaxWidth, Height));

    protected override void Place(Rect rect) { }

    protected override void Paint(PaintScope s)
    {
        var b = Bounds;
        if (b.Width <= 0f) return;
        float radius = Radius > 0f ? Radius : b.Height / 2f;

        if (TrackColor.A > 0f)
        {
            var track = s.BrushWithOpacity(new Color4(TrackColor.R, TrackColor.G, TrackColor.B, TrackColor.A * Opacity));
            var rr = new RoundedRectangle(new RectangleF(b.X, b.Y, b.Width, b.Height), radius, radius);
            s.Rt.FillRoundedRectangle(ref rr, track);
        }

        float pct = Math.Clamp(Progress / 100f, 0f, 1f);
        if (pct > 0f)
        {
            float w = MathF.Max(b.Height, b.Width * pct);
            var fill = s.BrushWithOpacity(new Color4(FillColor.R, FillColor.G, FillColor.B, FillColor.A * Opacity));
            var rr = new RoundedRectangle(new RectangleF(b.X, b.Y, w, b.Height), radius, radius);
            s.Rt.FillRoundedRectangle(ref rr, fill);
        }
    }
}

/// <summary>进度圆环（B 区 progressTextInfo）：未达整圆 + 已达圆弧，从 12 点方向顺时针。</summary>
internal sealed class ProgressRing : OverlayNode
{
    public float Diameter = 20f;
    public float StrokeWidth = 2.5f;
    public int Progress;
    public Color4 ReachColor = new(0.3f, 0.7f, 1f, 1f);
    public Color4 UnReachColor = new(0.35f, 0.35f, 0.35f, 1f);
    public float Opacity = 1f;

    protected override Size Measure(MeasureScope s, Constraints c)
        => c.Constrain(new Size(Diameter, Diameter));

    protected override void Place(Rect rect) { }

    protected override void Paint(PaintScope s)
    {
        var b = Bounds;
        float r = Diameter / 2f;
        var center = new Vector2(b.X + r, b.Y + r);
        var ring = new Ellipse(center, r, r);

        var unReach = s.BrushWithOpacity(new Color4(UnReachColor.R, UnReachColor.G, UnReachColor.B, UnReachColor.A * Opacity));
        s.Rt.DrawEllipse(ring, unReach, StrokeWidth);

        if (Progress > 0 && Progress < 100)
        {
            var reach = s.BrushWithOpacity(new Color4(ReachColor.R, ReachColor.G, ReachColor.B, ReachColor.A * Opacity));
            DrawArc(s, center, r, Progress / 100f * 360f, reach, StrokeWidth);
        }
        else if (Progress >= 100)
        {
            var reach = s.BrushWithOpacity(new Color4(ReachColor.R, ReachColor.G, ReachColor.B, ReachColor.A * Opacity));
            s.Rt.DrawEllipse(ring, reach, StrokeWidth);
        }
    }

    /// <summary>以 12 点方向为起点顺时针绘制指定角度（度）的圆弧描边。</summary>
    private static void DrawArc(PaintScope s, Vector2 center, float radius, float sweepDegrees,
        ID2D1Brush brush, float strokeWidth)
    {
        float startAngle = -90f * MathF.PI / 180f;
        float sweepRad = sweepDegrees * MathF.PI / 180f;
        var start = new Vector2(center.X + radius * MathF.Cos(startAngle), center.Y + radius * MathF.Sin(startAngle));
        var end = new Vector2(center.X + radius * MathF.Cos(startAngle + sweepRad), center.Y + radius * MathF.Sin(startAngle + sweepRad));

        using var geometry = s.D2DFactory.CreatePathGeometry();
        using (var sink = geometry.Open())
        {
            sink.BeginFigure(start, FigureBegin.Hollow);
            sink.AddArc(new D2DArcSegment(end, new Vortice.Mathematics.Size(radius, radius), 0,
                SweepDirection.Clockwise,
                sweepDegrees > 180 ? ArcSize.Large : ArcSize.Small));
            sink.EndFigure(FigureEnd.Open);
            sink.Close();
        }
        s.Rt.DrawGeometry(geometry, brush, strokeWidth);
    }
}

/// <summary>
/// 跑马灯文本：短文本直接绘制；文本超出可用宽度时在裁剪框内横向滚动循环。
/// 滚动偏移取 <see cref="PaintScope.NowSeconds"/>（逐帧值不触发重组），
/// 锚点由 <see cref="AnchorSeconds"/> 提供（文本变化时由组合侧重置）。
/// </summary>
internal sealed class MarqueeText : OverlayNode
{
    // 与现有实现对齐的滚动参数
    private const double ScrollSpeed = 30.0;      // px/sec
    private const double StartDelay = 0.8;        // 起始停留秒数
    private const double EndPadding = 12.0;       // 末端留白
    private const float MeasureWidth = 10000f;

    public string TextValue = string.Empty;
    public string FontFamily = "Microsoft YaHei";
    public DWriteFontWeight Weight = DWriteFontWeight.Normal;
    public float FontSize = 12f;
    public float LineHeight;
    public float MaxWidth;
    /// <summary>上报给父容器的行高（0 = 使用 <see cref="LineHeight"/>）。</summary>
    public float ReportedHeight;
    public Color4 Color = new(1f, 1f, 1f, 1f);
    public float Opacity = 1f;
    /// <summary>滚动锚点（秒，来自 Stopwatch 时间戳换算），文本或播放状态变化时由组合侧更新。</summary>
    public double AnchorSeconds;
    /// <summary>为 true 才允许滚动（播放中）；否则超宽以省略号截断。</summary>
    public bool ScrollEnabled = true;

    private TextLayoutCache _cache = new();

    protected override Size Measure(MeasureScope s, Constraints c)
    {
        float maxW = MaxWidth > 0f ? MathF.Min(MaxWidth, c.MaxWidth) : c.MaxWidth;
        if (float.IsPositiveInfinity(maxW)) maxW = 10000f;
        float lineH = LineHeight > 0f ? LineHeight : FontSize * 1.4f;

        // 用一个足够大的宽度量测完整文本，避免按可用宽度截断后无法判断溢出
        _cache.Ensure(s, TextValue, FontFamily, Weight, FontSize, MeasureWidth, lineH, false);
        float h = ReportedHeight > 0f ? ReportedHeight : lineH;
        return c.Constrain(new Size(MathF.Min(_cache.UnconstrainedWidth, maxW), h));
    }

    protected override void Place(Rect rect) { }

    protected override void Paint(PaintScope s)
    {
        var layout = _cache.Layout;
        if (layout == null || string.IsNullOrEmpty(TextValue)) return;

        var b = Bounds;
        if (b.Width <= 0f) return;

        float fullWidth = _cache.UnconstrainedWidth;
        float overflow = fullWidth - b.Width;
        var brush = s.BrushWithOpacity(new Color4(Color.R, Color.G, Color.B, Color.A * Opacity));
        float lineH = LineHeight > 0f ? LineHeight : FontSize * 1.4f;

        if (ScrollEnabled && overflow > 2f)
        {
            float offset = ComputeOffset(s.NowSeconds - AnchorSeconds, overflow);
            var clip = new Rect(b.X, b.Y - 2f, b.Width, lineH + 4f);
            s.PushClip(clip);
            try
            {
                s.Rt.DrawTextLayout(new Vector2(b.X + offset, b.Y), layout, brush);
            }
            finally
            {
                s.PopClip();
            }
            return;
        }

        // 非滚动：裁剪 + 省略号截断（绝不超出右边界）
        var truncClip = new Rect(b.X, b.Y - 2f, b.Width, lineH + 4f);
        s.PushClip(truncClip);
        try
        {
            using var format = s.CreateTextFormat(FontFamily, Weight, FontSize);
            using var truncated = s.CreateTruncatedLayout(TextValue, format, MathF.Max(1f, b.Width), lineH);
            s.Rt.DrawTextLayout(new Vector2(b.X, b.Y), truncated, brush);
        }
        finally
        {
            s.PopClip();
        }
    }

    /// <summary>
    /// 计算横向偏移，与 Gamebar 的 TranslateTransform + DoubleAnimationUsingKeyFrames 等价：
    /// 前 StartDelay 秒停留原位，随后以 ScrollSpeed 匀速向左滚动 (overflow + EndPadding) 距离并循环。
    /// </summary>
    public static float ComputeOffset(double elapsedSeconds, float overflow)
    {
        if (overflow <= 2f) return 0f;
        double scrollDistance = overflow + EndPadding;
        double duration = Math.Max(scrollDistance / ScrollSpeed, 1.2);
        double total = StartDelay + duration;
        double t = elapsedSeconds % total;
        if (t < 0) t += total;
        if (t <= StartDelay) return 0f;
        double p = (t - StartDelay) / duration;
        return -(float)(scrollDistance * p);
    }

    protected override void OnDispose()
    {
        _cache.Dispose();
        _cache = new TextLayoutCache();
    }
}

/// <summary>矩形裁剪容器：子节点绘制被限制在本节点矩形内。</summary>
internal sealed class Clip : ContainerNode
{
    private OverlayNode? Child => Children.Count > 0 ? Children[0] : null;

    protected override Size Measure(MeasureScope s, Constraints c)
        => Child?.MeasureNode(s, c) ?? Size.Zero;

    protected override void Place(Rect rect)
    {
        Child?.PlaceNode(rect);
    }

    protected override void Paint(PaintScope s)
    {
        if (Bounds.IsEmpty) return;
        s.PushClip(Bounds);
        try
        {
            PaintChildren(s);
        }
        finally
        {
            s.PopClip();
        }
    }
}

/// <summary>圆形裁剪容器（chatInfo 头像）。</summary>
internal sealed class CircleClip : ContainerNode
{
    public float Diameter;

    private OverlayNode? Child => Children.Count > 0 ? Children[0] : null;

    protected override Size Measure(MeasureScope s, Constraints c)
    {
        Child?.MeasureNode(s, new Constraints(Diameter, Diameter));
        return c.Constrain(new Size(Diameter, Diameter));
    }

    protected override void Place(Rect rect)
    {
        Child?.PlaceNode(new Rect(rect.X, rect.Y, Diameter, Diameter));
    }

    protected override void Paint(PaintScope s)
    {
        if (Child == null) return;
        var b = Bounds;
        float r = Diameter / 2f;
        var ellipse = new Ellipse(new Vector2(b.X + r, b.Y + r), r, r);
        using var geometry = s.D2DFactory.CreateEllipseGeometry(ellipse);
        var layerParams = new LayerParameters
        {
            ContentBounds = new RectangleF(b.X, b.Y, Diameter, Diameter),
            GeometricMask = geometry,
            MaskAntialiasMode = AntialiasMode.PerPrimitive
        };
        using var layer = s.Rt.CreateLayer();
        s.Rt.PushLayer(layerParams, layer);
        try
        {
            PaintChildren(s);
        }
        finally
        {
            s.Rt.PopLayer();
        }
    }
}

/// <summary>
/// 圆角标签块（specialTitle / HighLightText）：固定内边距 + 圆角背景 + 文本。
/// </summary>
internal sealed class Tag : ContainerNode
{
    public float Radius = 4f;
    public Color4 Background = new(0.3f, 0.3f, 0.3f, 1f);
    public float BackgroundOpacity = 0.8f;
    public Insets Insets = new(6f, 1f, 6f, 1f);
    public float MinHeight;

    private OverlayNode? Child => Children.Count > 0 ? Children[0] : null;

    protected override Size Measure(MeasureScope s, Constraints c)
    {
        if (Child == null) return c.Constrain(new Size(Insets.Horizontal, MinHeight));
        var size = Child.MeasureNode(s, new Constraints(
            float.IsPositiveInfinity(c.MaxWidth) ? c.MaxWidth : MathF.Max(0f, c.MaxWidth - Insets.Horizontal),
            float.PositiveInfinity));
        float h = MathF.Max(MinHeight, size.Height + Insets.Vertical);
        return c.Constrain(new Size(size.Width + Insets.Horizontal, h));
    }

    protected override void Place(Rect rect)
    {
        Child?.PlaceNode(rect.Deflate(Insets));
    }

    protected override void Paint(PaintScope s)
    {
        var b = Bounds;
        if (b.IsEmpty) { PaintChildren(s); return; }
        var brush = s.BrushWithOpacity(new Color4(Background.R, Background.G, Background.B, Background.A * BackgroundOpacity));
        var rr = new RoundedRectangle(b.ToRectangleF(), Radius, Radius);
        s.Rt.FillRoundedRectangle(ref rr, brush);
        PaintChildren(s);
    }
}

/// <summary>
/// 按钮（actions / highlightInfoV3 / hintInfo）：圆角背景 + 居中文本，支持固定高度。
/// </summary>
internal sealed class Button : ContainerNode
{
    public float Radius = 8f;
    public float Height = 30f;
    public Color4 Background = new(0.22f, 0.22f, 0.22f, 1f);
    public float BackgroundOpacity = 0.8f;
    public Insets Insets = new(6f, 0f, 6f, 0f);
    public Color4 TextColor = new(0.29f, 0.56f, 0.94f, 1f);
    public string TextValue = string.Empty;
    public float FontSize = 14f;

    protected override Size Measure(MeasureScope s, Constraints c)
    {
        // 按钮宽度由父容器（等分 Spacer / FixedWidth）决定，这里只上报高度与最小宽度
        float w = float.IsPositiveInfinity(c.MaxWidth) ? 0f : c.MaxWidth;
        return c.Constrain(new Size(w, Height));
    }

    protected override void Place(Rect rect) { }

    protected override void Paint(PaintScope s)
    {
        var b = Bounds;
        if (b.Width <= 0f) return;

        var bg = s.BrushWithOpacity(new Color4(Background.R, Background.G, Background.B, Background.A * BackgroundOpacity));
        var rr = new RoundedRectangle(b.ToRectangleF(), Radius, Radius);
        s.Rt.FillRoundedRectangle(ref rr, bg);

        if (string.IsNullOrEmpty(TextValue)) return;

        using var format = s.CreateTextFormat("Microsoft YaHei", DWriteFontWeight.Normal, FontSize);
        using var layout = s.CreateTruncatedLayout(TextValue, format,
            MathF.Max(1f, b.Width - Insets.Horizontal), 18f);
        var brush = s.BrushWithOpacity(TextColor);
        float textY = b.Y + (b.Height - 18f) / 2f;
        s.Rt.DrawTextLayout(new Vector2(b.X + Insets.Left, textY), layout, brush);
    }
}

/// <summary>
/// 不透明度包装：把 <see cref="OpacityProvider"/> 的值乘入累积不透明度，
/// 用于逐帧淡出（键盘映射提示）等不该触发重组的动态效果。
/// </summary>
internal sealed class OpacityBox : ContainerNode
{
    /// <summary>不透明度提供者（每帧求值）。</summary>
    public Func<float>? OpacityProvider;
    /// <summary>隐式不透明度（与提供者结果相乘）。</summary>
    public float BaseOpacity = 1f;

    private OverlayNode? Child => Children.Count > 0 ? Children[0] : null;

    protected override Size Measure(MeasureScope s, Constraints c)
        => Child?.MeasureNode(s, c) ?? Size.Zero;

    protected override void Place(Rect rect)
    {
        Child?.PlaceNode(rect);
    }

    protected override void Paint(PaintScope s)
    {
        float previous = s.Opacity;
        float factor = OpacityProvider?.Invoke() ?? 1f;
        s.Opacity = previous * BaseOpacity * Math.Clamp(factor, 0f, 1f);
        try
        {
            PaintChildren(s);
        }
        finally
        {
            s.Opacity = previous;
        }
    }
}
