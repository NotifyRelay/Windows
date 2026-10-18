using System.Drawing;

namespace NotifyRelay.Services.Overlay.UI;

/// <summary>二维尺寸（与浮点像素对齐）。</summary>
internal readonly struct Size
{
    public readonly float Width;
    public readonly float Height;

    public Size(float width, float height)
    {
        Width = width;
        Height = height;
    }

    public static readonly Size Zero = new(0, 0);

    public bool IsEmpty => Width <= 0f || Height <= 0f;

    public override string ToString() => $"Size({Width:0.##}, {Height:0.##})";
}

/// <summary>轴对齐矩形（左上角 + 尺寸）。</summary>
internal readonly struct Rect
{
    public readonly float X;
    public readonly float Y;
    public readonly float Width;
    public readonly float Height;

    public Rect(float x, float y, float width, float height)
    {
        X = x;
        Y = y;
        Width = width;
        Height = height;
    }

    public static readonly Rect Empty = new(0, 0, 0, 0);

    public float Right => X + Width;
    public float Bottom => Y + Height;
    public Size Size => new(Width, Height);
    public bool IsEmpty => Width <= 0f || Height <= 0f;

    public Rect Offset(float dx, float dy) => new(X + dx, Y + dy, Width, Height);

    public Rect Inflate(float dx, float dy) => new(X - dx, Y - dy, Width + dx * 2, Height + dy * 2);

    /// <summary>按内边距收缩（用于 padding 后的内容区）。</summary>
    public Rect Deflate(Insets insets)
        => new(X + insets.Left, Y + insets.Top,
            Math.Max(0f, Width - insets.Horizontal), Math.Max(0f, Height - insets.Vertical));

    public RectangleF ToRectangleF() => new(X, Y, Width, Height);

    public override string ToString() => $"Rect({X:0.##}, {Y:0.##}, {Width:0.##}, {Height:0.##})";
}

/// <summary>四向内边距。</summary>
internal readonly struct Insets
{
    public readonly float Left;
    public readonly float Top;
    public readonly float Right;
    public readonly float Bottom;

    public Insets(float left, float top, float right, float bottom)
    {
        Left = left;
        Top = top;
        Right = right;
        Bottom = bottom;
    }

    public Insets(float uniform) : this(uniform, uniform, uniform, uniform) { }

    public Insets(float horizontal, float vertical)
        : this(horizontal, vertical, horizontal, vertical) { }

    public static readonly Insets Zero = new(0);

    public float Horizontal => Left + Right;
    public float Vertical => Top + Bottom;

    public override string ToString() => $"Insets({Left:0.##}, {Top:0.##}, {Right:0.##}, {Bottom:0.##})";
}

/// <summary>
/// 测量约束：父节点下发给子节点的可用空间。
/// Max 为 <see cref="float.PositiveInfinity"/> 表示该轴不限（内容自适应）。
/// </summary>
internal readonly struct Constraints
{
    public readonly float MaxWidth;
    public readonly float MaxHeight;

    public Constraints(float maxWidth, float maxHeight)
    {
        MaxWidth = maxWidth;
        MaxHeight = maxHeight;
    }

    /// <summary>不限宽度、限定高度的约束（多数模板内容的自适应场景）。</summary>
    public static Constraints LooseHeight(float maxWidth)
        => new(maxWidth, float.PositiveInfinity);

    public static Constraints Unbounded => new(float.PositiveInfinity, float.PositiveInfinity);

    /// <summary>钳制子节点回报的尺寸到本约束。</summary>
    public Size Constrain(Size size)
        => new(MathF.Min(size.Width, MaxWidth), MathF.Min(size.Height, MaxHeight));
}

/// <summary>主轴 / 交叉轴对齐方式。</summary>
internal enum MainAlignment
{
    Start,
    Center,
    End
}

/// <summary>交叉轴对齐方式（Column 的横向 / Row 的纵向）。</summary>
internal enum CrossAlignment
{
    Start,
    Center,
    End,
    Stretch
}

/// <summary>容器主轴方向（供 Align 等节点判断百分比锚点语义）。</summary>
internal enum Axis
{
    Horizontal,
    Vertical
}
