using System.Numerics;
using System.Drawing;
using Vortice;
using Vortice.Direct2D1;
using Vortice.DirectWrite;
using Vortice.Mathematics;
using DWriteFontStretch = Vortice.DirectWrite.FontStretch;
using DWriteFontStyle = Vortice.DirectWrite.FontStyle;
using DWriteFontWeight = Vortice.DirectWrite.FontWeight;

namespace NotifyRelay.Services.Overlay.UI;

/// <summary>
/// 绘制上下文：一次 Paint 遍历内所有节点共享的绘制环境。
/// 承载渲染目标与三工厂、逐帧动态值（<see cref="Now"/>/<see cref="Freq"/>）、
/// 累积不透明度与变换/裁剪栈，并提供按 rt 归属的画刷缓存入口。
/// 每帧构造一次，构造本身不分配 D2D 资源（画刷缓存由 <see cref="OverlayUiRoot"/> 持有跨帧复用）。
/// </summary>
internal sealed class PaintScope
{
    /// <summary>量化颜色键的精度：每通道 1/255 即精确到 8bit，避免浮点误差导致缓存击穿。</summary>
    private const float ColorQuantScale = 255f;

    private readonly BrushCache _brushes;
    private readonly List<Matrix3x2> _transformStack = new(4);

    public PaintScope(ID2D1DCRenderTarget rt, IDWriteFactory dwFactory, ID2D1Factory d2dFactory,
        BrushCache brushes, double now, double freq)
    {
        Rt = rt;
        DwFactory = dwFactory;
        D2DFactory = d2dFactory;
        _brushes = brushes;
        Now = now;
        Freq = freq;
        Opacity = 1f;
    }

    public ID2D1DCRenderTarget Rt { get; }

    public IDWriteFactory DwFactory { get; }

    public ID2D1Factory D2DFactory { get; }

    /// <summary>帧时间戳（Stopwatch.GetTimestamp()），供跑马灯 / 心跳 / 频谱等逐帧值使用。</summary>
    public double Now { get; }

    /// <summary>Stopwatch.Frequency，与 <see cref="Now"/> 配合换算秒数。</summary>
    public double Freq { get; }

    /// <summary>累积不透明度：OpacityProvider 节点在此范围内缩放，子节点继承。</summary>
    public float Opacity { get; set; }

    /// <summary>当前帧时间（秒），供逐帧动画使用。</summary>
    public double NowSeconds => Now / Freq;

    /// <summary>取（或创建并缓存）指定颜色的纯色画刷。缓存按 rt 归属，rt 变化时整表重建。</summary>
    public ID2D1SolidColorBrush Brush(Color4 color) => _brushes.Get(Rt, color);

    /// <summary>取画刷，并把颜色 alpha 乘以当前累积不透明度。</summary>
    public ID2D1SolidColorBrush BrushWithOpacity(Color4 color)
        => Brush(new Color4(color.R, color.G, color.B, color.A * Opacity));

    /// <summary>把颜色 alpha 乘以累积不透明度（用于需要拿到 Color4 而非画刷的场景）。</summary>
    public Color4 ApplyOpacity(Color4 color) => new(color.R, color.G, color.B, color.A * Opacity);

    /// <summary>量化颜色键（供外部缓存使用，如节点自建几何画刷）。</summary>
    public static uint QuantizeColor(Color4 color) => Quantize(color);

    internal static uint Quantize(Color4 color)
    {
        uint r = (uint)Math.Clamp((int)MathF.Round(color.R * ColorQuantScale), 0, 255);
        uint g = (uint)Math.Clamp((int)MathF.Round(color.G * ColorQuantScale), 0, 255);
        uint b = (uint)Math.Clamp((int)MathF.Round(color.B * ColorQuantScale), 0, 255);
        uint a = (uint)Math.Clamp((int)MathF.Round(color.A * ColorQuantScale), 0, 255);
        return (a << 24) | (r << 16) | (g << 8) | b;
    }

    // ---------- 变换栈 ----------

    public void PushTransform(Matrix3x2 transform)
    {
        _transformStack.Add(Rt.Transform);
        Rt.Transform = transform;
    }

    /// <summary>在当前变换基础上叠加平移（保持已生效的变换）。</summary>
    public void PushTranslation(Vector2 offset)
    {
        _transformStack.Add(Rt.Transform);
        Rt.Transform = Rt.Transform * Matrix3x2.CreateTranslation(offset);
    }

    public void PopTransform()
    {
        if (_transformStack.Count == 0) return;
        Rt.Transform = _transformStack[^1];
        _transformStack.RemoveAt(_transformStack.Count - 1);
    }

    /// <summary>压入轴对齐裁剪区。</summary>
    public void PushClip(Rect rect)
        => Rt.PushAxisAlignedClip(
            new RawRectF(rect.X, rect.Y, rect.Right, rect.Bottom), AntialiasMode.Aliased);

    public void PopClip() => Rt.PopAxisAlignedClip();

    // ---------- 文本工厂（rt 无关，可缓存在节点槽位） ----------

    /// <summary>创建文本格式（统一 Normal 字型/拉伸）。</summary>
    public IDWriteTextFormat CreateTextFormat(string fontFamily, DWriteFontWeight weight, float size)
        => DwFactory.CreateTextFormat(fontFamily, null!, weight, DWriteFontStyle.Normal, DWriteFontStretch.Normal, size);

    /// <summary>创建单行、超出以字符级尾随省略号截断的文本布局。</summary>
    public IDWriteTextLayout CreateTruncatedLayout(string text, IDWriteTextFormat format,
        float maxWidth, float maxHeight)
    {
        var layout = DwFactory.CreateTextLayout(text, format, Math.Max(1f, maxWidth), maxHeight);
        layout.WordWrapping = WordWrapping.NoWrap;
        using var ellipsis = DwFactory.CreateEllipsisTrimmingSign(format);
        layout.SetTrimming(
            new Trimming { Granularity = TrimmingGranularity.Character, Delimiter = 0, DelimiterCount = 0 },
            ellipsis);
        return layout;
    }

    /// <summary>创建单行、不换行的文本布局（宽度不限，用于精确量测）。</summary>
    public IDWriteTextLayout CreateMeasureLayout(string text, IDWriteTextFormat format, float measureWidth)
    {
        var layout = DwFactory.CreateTextLayout(text, format, measureWidth, float.PositiveInfinity);
        layout.WordWrapping = WordWrapping.NoWrap;
        return layout;
    }

    /// <summary>量测单行文本宽度（内部临时资源，测完即释放）。</summary>
    public float MeasureTextWidth(string text, string fontFamily, DWriteFontWeight weight, float size)
    {
        if (string.IsNullOrEmpty(text)) return 0f;
        using var format = CreateTextFormat(fontFamily, weight, size);
        using var layout = CreateMeasureLayout(text, format, 10000f);
        return layout.Metrics.WidthIncludingTrailingWhitespace;
    }
}

/// <summary>
/// 按 (渲染目标, 量化颜色) 缓存的纯色画刷表。
/// <see cref="ID2D1SolidColorBrush"/> 与创建它的渲染目标绑定，故 rt 变化时整表释放重建 —— 
/// 全运行时只此一处规则，替代各元素各自手写的 rtChanged 分支。
/// 仅渲染线程访问，无需加锁。
/// </summary>
internal sealed class BrushCache : IDisposable
{
    private readonly Dictionary<uint, ID2D1SolidColorBrush> _brushes = new(32);
    private ID2D1DCRenderTarget? _rt;

    /// <summary>取画刷；rt 变化时先整表重建。</summary>
    public ID2D1SolidColorBrush Get(ID2D1DCRenderTarget rt, Color4 color)
    {
        if (!ReferenceEquals(_rt, rt))
            Reset(rt);

        uint key = PaintScope.Quantize(color);
        if (_brushes.TryGetValue(key, out var brush))
            return brush;

        // 量化后按量化值建刷，保证缓存键与实际颜色一致（避免同键不同色的漂移）
        var quantized = new Color4(
            ((key >> 16) & 0xFF) / 255f,
            ((key >> 8) & 0xFF) / 255f,
            (key & 0xFF) / 255f,
            ((key >> 24) & 0xFF) / 255f);
        brush = rt.CreateSolidColorBrush(quantized);
        _brushes[key] = brush;
        return brush;
    }

    /// <summary>切换到新的渲染目标：释放旧画刷并记录新 rt。</summary>
    public void Reset(ID2D1DCRenderTarget? rt)
    {
        foreach (var brush in _brushes.Values)
        {
            try { brush.Dispose(); } catch { /* 释放失败不影响重建 */ }
        }
        _brushes.Clear();
        _rt = rt;
    }

    /// <summary>清空缓存（渲染目标失效 / 覆盖层清理时调用）。</summary>
    public void Clear() => Reset(null);

    public void Dispose() => Clear();
}
