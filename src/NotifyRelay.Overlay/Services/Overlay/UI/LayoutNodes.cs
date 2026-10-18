using System.Drawing;
using Vortice.Direct2D1;
using Vortice.Mathematics;
using DWriteFontWeight = Vortice.DirectWrite.FontWeight;

namespace NotifyRelay.Services.Overlay.UI;

/// <summary>可容纳子节点的节点基类。</summary>
internal abstract class ContainerNode : OverlayNode
{
    /// <summary>按声明顺序绘制全部子节点。</summary>
    protected void PaintChildren(PaintScope s)
    {
        for (int i = 0; i < Children.Count; i++)
            Children[i].PaintNode(s);
    }
}

/// <summary>
/// 每屏一棵的 UI 树根：尺寸由覆盖层窗口给定，子节点按全屏矩形落位。
/// </summary>
internal sealed class UiRootNode : ContainerNode
{
    /// <summary>本帧的根尺寸（屏宽高）。</summary>
    public float Width;
    public float Height;

    protected override Size Measure(MeasureScope s, Constraints c)
    {
        Width = c.MaxWidth;
        Height = c.MaxHeight;
        for (int i = 0; i < Children.Count; i++)
            Children[i].MeasureNode(s, c);
        return new Size(Width, Height);
    }

    protected override void Place(Rect rect)
    {
        var full = new Rect(rect.X, rect.Y, Width, Height);
        for (int i = 0; i < Children.Count; i++)
            Children[i].PlaceNode(full);
    }

    protected override void Paint(PaintScope s) => PaintChildren(s);
}

/// <summary>
/// 纵向排列（等价现有模板里手工累加的 cursorY）。
/// <see cref="UniformWidth"/> 复刻罗技电池「取最大自然宽后统一卡片宽」的现状。
/// </summary>
internal sealed class Column : ContainerNode
{
    public float Spacing;
    public CrossAlignment CrossAlignment = CrossAlignment.Start;
    /// <summary>为 true 时所有子节点使用本列最终宽度（等宽列）。</summary>
    public bool UniformWidth;
    /// <summary>
    /// 为 true 时本列上报宽度取满约束宽度，但子节点仍按各自自然宽度量测。
    /// 用于「子卡片各自在屏幕上水平居中」的顶部卡片列：列占满屏宽，
    /// 配合 <see cref="CrossAlignment"/> = Center 即得逐卡片屏幕居中（对齐现状 (screenW - pillW)/2）。
    /// </summary>
    public bool FillAvailableWidth;
    /// <summary>整列内容的最大宽度上限（0 表示不限制）；UniformWidth 场景下用于卡片最大宽。</summary>
    public float MaxChildWidth;
    /// <summary>固定宽度（&gt;0 时覆盖由内容推导的宽度）。</summary>
    public float FixedWidth;

    protected override Size Measure(MeasureScope s, Constraints c)
    {
        float avail = c.MaxWidth;
        float maxW = 0f, totalH = 0f;
        float floatWeightSum = 0f;

        for (int i = 0; i < Children.Count; i++)
        {
            var child = Children[i];
            float weight = (child as Spacer)?.Weight ?? 0f;

            // 非加权 Spacer 作为固定间隔参与累加
            if (child is Spacer && weight <= 0f)
            {
                var gapSize = child.MeasureNode(s, new Constraints(avail, float.PositiveInfinity));
                totalH += gapSize.Height;
                if (gapSize.Width > maxW) maxW = gapSize.Width;
                continue;
            }
            if (weight > 0f) { floatWeightSum += weight; continue; }

            var size = child.MeasureNode(s, new Constraints(avail, float.PositiveInfinity));
            if (size.Width > maxW) maxW = size.Width;
            totalH += size.Height;
        }

        // 加权子节点：均分剩余高度（无有限高度约束时退化为 0 高）
        if (floatWeightSum > 0f)
        {
            float used = totalH + Spacing * Math.Max(0, Children.Count - 1);
            float remaining = float.IsPositiveInfinity(c.MaxHeight) ? 0f : MathF.Max(0f, c.MaxHeight - used);
            for (int i = 0; i < Children.Count; i++)
            {
                if (Children[i] is not Spacer sp2 || sp2.Weight <= 0f) continue;
                float h = remaining * (sp2.Weight / floatWeightSum);
                Children[i].MeasureNode(s, new Constraints(avail, h));
                totalH += h;
                sp2.MeasuredFraction = h;
            }
        }

        if (MaxChildWidth > 0f) maxW = MathF.Min(maxW, MaxChildWidth);
        float width = FixedWidth > 0f ? FixedWidth
            : FillAvailableWidth && !float.IsPositiveInfinity(c.MaxWidth) ? c.MaxWidth
            : maxW;
        if (float.IsPositiveInfinity(width)) width = 0f;

        float height = totalH + Spacing * Math.Max(0, Children.Count - 1);
        return c.Constrain(new Size(width, height));
    }

    protected override void Place(Rect rect)
    {
        float y = rect.Y;

        for (int i = 0; i < Children.Count; i++)
        {
            var child = Children[i];
            float h = child is Spacer sp && sp.Weight > 0f ? sp.MeasuredFraction : child.MeasuredSize.Height;
            float w = child.MeasuredSize.Width;

            float x = rect.X;
            float cw = w;
            switch (CrossAlignment)
            {
                case CrossAlignment.Center:
                    x = rect.X + (rect.Width - w) / 2f;
                    break;
                case CrossAlignment.End:
                    x = rect.Right - w;
                    break;
                case CrossAlignment.Stretch:
                    cw = rect.Width;
                    break;
                default:
                    // UniformWidth：所有子节点统一到本列最终宽度（罗技电池等宽卡片）
                    if (UniformWidth) cw = rect.Width;
                    break;
            }

            child.PlaceNode(new Rect(x, y, cw, h));
            y += h + Spacing;
        }
    }

    protected override void Paint(PaintScope s) => PaintChildren(s);
}

/// <summary>横向排列（含加权等分，供按钮行使用）。</summary>
internal sealed class Row : ContainerNode
{
    public float Gap;
    public CrossAlignment CrossAlignment = CrossAlignment.Center;
    /// <summary>为 true 时所有子节点等分可用宽度（复刻按钮行 btnW = (avail - gap*(count-1))/count）。</summary>
    public bool EqualWidth;
    /// <summary>
    /// 为 true 时，若本行矩形宽于内容的自然宽度，则把剩余宽度整体插入到<b>最后一个子节点之前</b>：
    /// 首项仍贴左、末项贴右，空白落在两者之间而非右边界。
    /// 用于收起态胶囊被 MinWidth 撑宽（短文本）时消化多余空白，避免末项与右边界之间出现空隙。
    /// </summary>
    public bool AlignLastToEnd;

    protected override Size Measure(MeasureScope s, Constraints c)
    {
        if (EqualWidth && !float.IsPositiveInfinity(c.MaxWidth) && Children.Count > 0)
        {
            float each = MathF.Max(0f, (c.MaxWidth - Gap * (Children.Count - 1)) / Children.Count);
            float maxH = 0f;
            for (int i = 0; i < Children.Count; i++)
            {
                var size = Children[i].MeasureNode(s, new Constraints(each, c.MaxHeight));
                maxH = MathF.Max(maxH, size.Height);
            }
            return c.Constrain(new Size(c.MaxWidth, maxH));
        }

        float maxHeight = 0f, totalW = 0f, weightSum = 0f;

        for (int i = 0; i < Children.Count; i++)
        {
            var child = Children[i];
            float weight = (child as Spacer)?.Weight ?? 0f;
            if (weight > 0f) { weightSum += weight; continue; }

            var size = child.MeasureNode(s, new Constraints(c.MaxWidth, c.MaxHeight));
            totalW += size.Width;
            if (size.Height > maxHeight) maxHeight = size.Height;
        }

        if (weightSum > 0f)
        {
            float used = totalW + Gap * Math.Max(0, Children.Count - 1);
            float remaining = float.IsPositiveInfinity(c.MaxWidth) ? 0f : MathF.Max(0f, c.MaxWidth - used);
            for (int i = 0; i < Children.Count; i++)
            {
                if (Children[i] is not Spacer sp || sp.Weight <= 0f) continue;
                float w = remaining * (sp.Weight / weightSum);
                Children[i].MeasureNode(s, new Constraints(w, c.MaxHeight));
                sp.MeasuredFraction = w;
                totalW += w;
            }
        }

        float width = totalW + Gap * Math.Max(0, Children.Count - 1);
        return c.Constrain(new Size(width, maxHeight));
    }

    protected override void Place(Rect rect)
    {
        float x = rect.X;
        int n = Children.Count;
        float equalW = EqualWidth && n > 0
            ? MathF.Max(0f, (rect.Width - Gap * (n - 1)) / n)
            : 0f;

        // AlignLastToEnd：自然宽度不足本行矩形宽时，把剩余宽度整体插入末项之前，
        // 使首项贴左、末项贴右，空白落在两者之间而非右边界（仅收起态胶囊被 MinWidth 撑宽时触发）。
        float extraBeforeLast = 0f;
        if (AlignLastToEnd && !EqualWidth && n > 1)
        {
            float natural = Gap * (n - 1);
            for (int i = 0; i < n; i++)
            {
                var c = Children[i];
                natural += c is Spacer sp0 && sp0.Weight > 0f ? sp0.MeasuredFraction : c.MeasuredSize.Width;
            }
            extraBeforeLast = MathF.Max(0f, rect.Width - natural);
        }

        for (int i = 0; i < n; i++)
        {
            var child = Children[i];
            float w = EqualWidth ? equalW
                : child is Spacer sp && sp.Weight > 0f ? sp.MeasuredFraction
                : child.MeasuredSize.Width;
            float h = child.MeasuredSize.Height;
            float y = rect.Y;

            if (i == n - 1) x += extraBeforeLast;

            if (CrossAlignment == CrossAlignment.Center) y = rect.Y + (rect.Height - h) / 2f;
            else if (CrossAlignment == CrossAlignment.End) y = rect.Bottom - h;
            else if (CrossAlignment == CrossAlignment.Stretch) h = rect.Height;

            child.PlaceNode(new Rect(x, y, w, h));
            x += w + Gap;
        }
    }

    protected override void Paint(PaintScope s) => PaintChildren(s);
}

/// <summary>
/// 自动换行行（键盘按键框：等宽格 + 间距，超过 maxWidth 换行）。
/// </summary>
internal sealed class WrapRow : ContainerNode
{
    public float Gap;
    public float RowGap;
    /// <summary>单行最大宽度（&lt;=0 时取约束宽度）。</summary>
    public float MaxWidth;
    /// <summary>
    /// 换行判据按「下一个元素的左边缘是否达到 <see cref="MaxWidth"/>」而非「元素是否装得下」。
    /// 用于复刻键盘按键框的现状算式 <c>(nextX - KeyStartX) / (KeyBoxSize + KeyBoxMargin) &gt;= N</c>：
    /// 该算式比较的是格位序号，因此宽键（Shift / Space）可以越过 MaxWidth 而不提前换行。
    /// </summary>
    public bool WrapOnNextLeftOffset;

    protected override Size Measure(MeasureScope s, Constraints c)
    {
        float maxWidth = MaxWidth > 0f ? MaxWidth : c.MaxWidth;
        if (float.IsPositiveInfinity(maxWidth)) maxWidth = 0f;

        float lineW = 0f, totalH = 0f, lineH = 0f, widest = 0f;
        int inLine = 0;

        for (int i = 0; i < Children.Count; i++)
        {
            var size = Children[i].MeasureNode(s,
                new Constraints(maxWidth > 0f ? maxWidth : c.MaxWidth, c.MaxHeight));

            // 该元素在当前行的左边缘偏移（不含自身宽度）
            float leftOffset = lineW + (inLine > 0 ? Gap : 0f);

            bool wrap = inLine > 0
                && maxWidth > 0f
                && (WrapOnNextLeftOffset
                    ? leftOffset >= maxWidth
                    : leftOffset + size.Width > maxWidth);

            if (wrap)
            {
                widest = MathF.Max(widest, lineW);
                totalH += lineH + RowGap;
                lineW = size.Width;
                lineH = size.Height;
                inLine = 1;
            }
            else
            {
                lineW += inLine > 0 ? Gap + size.Width : size.Width;
                lineH = MathF.Max(lineH, size.Height);
                inLine++;
            }
        }
        widest = MathF.Max(widest, lineW);
        totalH += lineH;
        return c.Constrain(new Size(widest, totalH));
    }

    protected override void Place(Rect rect)
    {
        float maxWidth = MaxWidth > 0f ? MaxWidth : rect.Width;
        float x = rect.X, y = rect.Y, lineH = 0f, lineStartX = rect.X;
        int inLine = 0;

        for (int i = 0; i < Children.Count; i++)
        {
            var child = Children[i];
            float w = child.MeasuredSize.Width;
            float h = child.MeasuredSize.Height;

            // 与 Measure 使用同一判据，保证换行位置一致
            float leftOffset = x - lineStartX;

            if (inLine > 0
                && maxWidth > 0f
                && (WrapOnNextLeftOffset
                    ? leftOffset >= maxWidth
                    : leftOffset + w > maxWidth))
            {
                x = rect.X;
                lineStartX = rect.X;
                y += lineH + RowGap;
                lineH = 0f;
                inLine = 0;
            }
            child.PlaceNode(new Rect(x, y, w, h));
            x += w + Gap;
            lineH = MathF.Max(lineH, h);
            inLine++;
        }
    }

    protected override void Paint(PaintScope s) => PaintChildren(s);
}

/// <summary>层叠：所有子节点落在同一矩形内（后声明的绘制在上层）。</summary>
internal sealed class Stack : ContainerNode
{
    protected override Size Measure(MeasureScope s, Constraints c)
    {
        float w = 0f, h = 0f;
        for (int i = 0; i < Children.Count; i++)
        {
            var size = Children[i].MeasureNode(s, c);
            w = MathF.Max(w, size.Width);
            h = MathF.Max(h, size.Height);
        }
        return c.Constrain(new Size(w, h));
    }

    protected override void Place(Rect rect)
    {
        for (int i = 0; i < Children.Count; i++)
            Children[i].PlaceNode(rect);
    }

    protected override void Paint(PaintScope s) => PaintChildren(s);
}

/// <summary>固定尺寸盒：内容按其自身尺寸居中放置（供图标固定显示区使用）。</summary>
internal sealed class Box : ContainerNode
{
    public float Width;
    public float Height;

    protected override Size Measure(MeasureScope s, Constraints c)
    {
        for (int i = 0; i < Children.Count; i++)
            Children[i].MeasureNode(s, new Constraints(Width, Height));
        return c.Constrain(new Size(Width, Height));
    }

    protected override void Place(Rect rect)
    {
        for (int i = 0; i < Children.Count; i++)
        {
            var child = Children[i];
            float w = child.MeasuredSize.Width;
            float h = child.MeasuredSize.Height;
            child.PlaceNode(new Rect(
                rect.X + (rect.Width - w) / 2f,
                rect.Y + (rect.Height - h) / 2f,
                w, h));
        }
    }

    protected override void Paint(PaintScope s) => PaintChildren(s);
}

/// <summary>单子节点内边距容器。</summary>
internal sealed class Padding : ContainerNode
{
    public Insets Insets = Insets.Zero;

    private OverlayNode? Child => Children.Count > 0 ? Children[0] : null;

    protected override Size Measure(MeasureScope s, Constraints c)
    {
        if (Child == null) return c.Constrain(new Size(Insets.Horizontal, Insets.Vertical));
        var inner = new Constraints(
            float.IsPositiveInfinity(c.MaxWidth) ? c.MaxWidth : MathF.Max(0f, c.MaxWidth - Insets.Horizontal),
            float.IsPositiveInfinity(c.MaxHeight) ? c.MaxHeight : MathF.Max(0f, c.MaxHeight - Insets.Vertical));
        var size = Child.MeasureNode(s, inner);
        return c.Constrain(new Size(size.Width + Insets.Horizontal, size.Height + Insets.Vertical));
    }

    protected override void Place(Rect rect)
    {
        Child?.PlaceNode(rect.Deflate(Insets));
    }

    protected override void Paint(PaintScope s) => PaintChildren(s);
}

/// <summary>
/// 圆角卡片表面：填充 + 边框 + 内边距（收敛罗技 / DSB / 超级岛重复的圆角卡片绘制）。
/// </summary>
internal sealed class Surface : ContainerNode
{
    public float Radius;
    public Color4 Fill = new(0f, 0f, 0f, 0f);
    public bool Filled;
    public Color4 Border = new(0f, 0f, 0f, 0f);
    public float BorderWidth;
    public bool Bordered;
    public Insets Insets = Insets.Zero;
    /// <summary>高度下限（0 = 完全由内容决定）。</summary>
    public float MinHeight;
    /// <summary>宽度下限（0 = 完全由内容决定）。</summary>
    public float MinWidth;
    /// <summary>单子节点在表面内水平 + 垂直居中（按键框 / 提示框）。</summary>
    public bool CenterContent;

    private OverlayNode? Child => Children.Count > 0 ? Children[0] : null;

    protected override Size Measure(MeasureScope s, Constraints c)
    {
        if (Child == null)
            return c.Constrain(new Size(
                MathF.Max(MinWidth, Insets.Horizontal),
                MathF.Max(MinHeight, Insets.Vertical)));
        var inner = new Constraints(
            float.IsPositiveInfinity(c.MaxWidth) ? c.MaxWidth : MathF.Max(0f, c.MaxWidth - Insets.Horizontal),
            float.IsPositiveInfinity(c.MaxHeight) ? c.MaxHeight : MathF.Max(0f, c.MaxHeight - Insets.Vertical));
        var size = Child.MeasureNode(s, inner);
        float h = MathF.Max(MinHeight, size.Height + Insets.Vertical);
        float w = MathF.Max(MinWidth, size.Width + Insets.Horizontal);
        return c.Constrain(new Size(w, h));
    }

    protected override void Place(Rect rect)
    {
        if (Child == null) return;
        var inner = rect.Deflate(Insets);
        if (!CenterContent)
        {
            Child.PlaceNode(inner);
            return;
        }
        float w = Child.MeasuredSize.Width;
        float h = Child.MeasuredSize.Height;
        Child.PlaceNode(new Rect(
            inner.X + (inner.Width - w) / 2f,
            inner.Y + (inner.Height - h) / 2f,
            w, h));
    }

    protected override void Paint(PaintScope s)
    {
        var b = Bounds;
        if (b.IsEmpty) { PaintChildren(s); return; }

        if (Filled)
        {
            var brush = s.BrushWithOpacity(Fill);
            var rr = new RoundedRectangle(b.ToRectangleF(), Radius, Radius);
            s.Rt.FillRoundedRectangle(ref rr, brush);
        }
        if (Bordered && BorderWidth > 0f)
        {
            var brush = s.BrushWithOpacity(Border);
            var rr = new RoundedRectangle(b.ToRectangleF(), Radius, Radius);
            s.Rt.DrawRoundedRectangle(rr, brush, BorderWidth);
        }
        PaintChildren(s);
    }
}

/// <summary>限制子节点宽度（min/max），高度随内容。</summary>
/// <summary>
/// 限制子节点宽度（min/max），高度随内容。
/// </summary>
internal sealed class Constrained : ContainerNode
{
    public float MinWidth;
    public float MaxWidth;
    /// <summary>固定宽度（&gt;0 时覆盖 min/max，内容按该宽度落位）。</summary>
    public float FixedWidth;
    /// <summary>固定高度（&gt;0 时覆盖内容高度）。</summary>
    public float FixedHeight;
    /// <summary>为 true 时在父容器内水平居中落位（宽度仍为自身自然/Fixed 宽度）。</summary>
    public bool HorizontallyCentered;

    private OverlayNode? Child => Children.Count > 0 ? Children[0] : null;

    protected override Size Measure(MeasureScope s, Constraints c)
    {
        if (Child == null)
            return c.Constrain(new Size(FixedWidth > 0f ? FixedWidth : MinWidth,
                FixedHeight > 0f ? FixedHeight : 0f));

        if (FixedWidth > 0f)
        {
            Child.MeasureNode(s, new Constraints(FixedWidth, float.PositiveInfinity));
            float fh = FixedHeight > 0f ? FixedHeight : Child.MeasuredSize.Height;
            return c.Constrain(new Size(FixedWidth, fh));
        }

        float upper = MaxWidth > 0f ? MathF.Min(MaxWidth, c.MaxWidth) : c.MaxWidth;
        var size = Child.MeasureNode(s, new Constraints(upper, c.MaxHeight));
        float w = size.Width;
        if (MinWidth > 0f) w = MathF.Max(w, MinWidth);
        w = MathF.Min(w, c.MaxWidth);
        float h = FixedHeight > 0f ? FixedHeight : size.Height;
        return c.Constrain(new Size(w, h));
    }

    protected override void Place(Rect rect)
    {
        if (Child == null) return;
        // 宽度受限时子节点用受限宽度落位（供文本省略号裁剪使用）
        float h = FixedHeight > 0f ? FixedHeight : Child.MeasuredSize.Height;
        float x = rect.X;
        if (HorizontallyCentered && rect.Width > MeasuredSize.Width)
            x = rect.X + (rect.Width - MeasuredSize.Width) / 2f;
        Child.PlaceNode(new Rect(x, rect.Y, rect.Width, h));
    }

    protected override void Paint(PaintScope s) => PaintChildren(s);
}

/// <summary>
/// 按父容器矩形的百分比锚点定位（元素中心），并夹取到容器内。
/// 与 <c>OverlayElementCore.ResolveAnchor</c> + 旧实现的 clamp 行为等价，
/// 收敛 5 个元素各自重复的锚点解析与夹取算式。
/// </summary>
internal sealed class Align : ContainerNode
{
    /// <summary>锚点横向百分比（0~100，相对父容器宽度）。</summary>
    public float XPct = 50f;
    /// <summary>锚点纵向百分比（0~100，相对父容器高度）。</summary>
    public float YPct = 50f;
    /// <summary>true：锚点为元素水平中心；false：锚点为元素左边缘。</summary>
    public bool AnchorAtCenterX = true;
    /// <summary>true：锚点为元素垂直中心；false：锚点为元素上边缘。</summary>
    public bool AnchorAtCenterY = true;
    /// <summary>相对锚点的固定像素偏移（键盘 20,20 等）。</summary>
    public float OffsetX;
    public float OffsetY;
    /// <summary>是否夹取到容器内（等价旧实现的 Math.Clamp）。</summary>
    public bool ClampToBounds = true;

    private OverlayNode? Child => Children.Count > 0 ? Children[0] : null;

    protected override Size Measure(MeasureScope s, Constraints c)
    {
        return Child?.MeasureNode(s, c) ?? Size.Zero;
    }

    protected override void Place(Rect rect)
    {
        if (Child == null) return;
        float w = Child.MeasuredSize.Width;
        float h = Child.MeasuredSize.Height;

        float anchorX = rect.X + rect.Width * Math.Clamp(XPct, 0f, 100f) / 100f + OffsetX;
        float anchorY = rect.Y + rect.Height * Math.Clamp(YPct, 0f, 100f) / 100f + OffsetY;

        float left = AnchorAtCenterX ? anchorX - w / 2f : anchorX;
        float top = AnchorAtCenterY ? anchorY - h / 2f : anchorY;

        if (ClampToBounds)
        {
            left = Math.Clamp(left, rect.X, rect.X + MathF.Max(0f, rect.Width - w));
            top = Math.Clamp(top, rect.Y, rect.Y + MathF.Max(0f, rect.Height - h));
        }
        Child.PlaceNode(new Rect(left, top, w, h));
    }

    protected override void Paint(PaintScope s) => PaintChildren(s);
}

/// <summary>占位 / 加权间隔节点。</summary>
internal sealed class Spacer : OverlayNode
{
    /// <summary>加权（&gt;0 时按剩余空间比例分配主轴尺寸）。</summary>
    public float Weight;
    /// <summary>固定高度（Column 中为主轴尺寸；0 = 由约束决定）。</summary>
    public float FixedHeight;
    /// <summary>固定宽度（Row 中为主轴尺寸；0 = 由约束决定）。</summary>
    public float FixedWidth;
    /// <summary>主轴方向被分配到的尺寸（Measure 阶段由父容器写入）。</summary>
    internal float MeasuredFraction;

    protected override Size Measure(MeasureScope s, Constraints c)
    {
        // 加权间隔（仅 Row/Column 的加权分配路径使用）：占满约束，实际尺寸由父容器回填。
        // 普通间隔只占显式给定的主轴尺寸，交叉轴上报 0 —— 否则会把父容器在该轴上撑满。
        if (Weight > 0f)
        {
            return new Size(
                float.IsPositiveInfinity(c.MaxWidth) ? 0f : c.MaxWidth,
                float.IsPositiveInfinity(c.MaxHeight) ? 0f : c.MaxHeight);
        }
        return new Size(FixedWidth, FixedHeight);
    }

    protected override void Place(Rect rect) { }

    protected override void Paint(PaintScope s) { }
}

/// <summary>
/// 不参与布局的绝对定位包装：把子节点放到「本节点矩形」内的指定偏移处。
/// 用于需要相对定位的模板局部（如右上角计时器）。
/// </summary>
internal sealed class Absolute : ContainerNode
{
    public float Left;
    public float Top;
    /// <summary>为 true 时 Left 相对父矩形右边（Right - Left - 子宽）。</summary>
    public bool FromRight;
    /// <summary>子节点尺寸不参与本节点尺寸计算（父容器已按主内容撑开时使用）。</summary>
    public bool IgnoreInMeasure = true;

    private OverlayNode? Child => Children.Count > 0 ? Children[0] : null;

    protected override Size Measure(MeasureScope s, Constraints c)
    {
        if (Child == null) return Size.Zero;
        var size = Child.MeasureNode(s, new Constraints(c.MaxWidth, float.PositiveInfinity));
        return IgnoreInMeasure ? new Size(0f, 0f) : size;
    }

    protected override void Place(Rect rect)
    {
        if (Child == null) return;
        float w = Child.MeasuredSize.Width;
        float h = Child.MeasuredSize.Height;
        float x = FromRight ? rect.Right - Left - w : rect.X + Left;
        Child.PlaceNode(new Rect(x, rect.Y + Top, w, h));
    }

    protected override void Paint(PaintScope s) => PaintChildren(s);
}
