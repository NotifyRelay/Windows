using System.Drawing;
using System.Numerics;
using NotifyRelay.Models.Render;
using Vortice;
using Vortice.Direct2D1;
using Vortice.DirectWrite;
using Vortice.Mathematics;
using BitmapInterpolationMode = Vortice.Direct2D1.BitmapInterpolationMode;
using DWriteFontStretch = Vortice.DirectWrite.FontStretch;
using DWriteFontStyle = Vortice.DirectWrite.FontStyle;
using DWriteFontWeight = Vortice.DirectWrite.FontWeight;

namespace NotifyRelay.Services.Overlay;

public partial class OverlayRenderService
{
    /// <summary>
    /// 绘制半透明圆角胶囊背景（统一填充色 new Color4(0,0,0,0.65f*opacity)）。
    /// </summary>
    private static void DrawPillBackground(ID2D1DCRenderTarget rt, float x, float y, float width, float height, float radius, float opacity)
    {
        using var bgBrush = rt.CreateSolidColorBrush(new Color4(0, 0, 0, 0.65f * opacity));
        var pillRR = new RoundedRectangle(new RectangleF(x, y, width, height), radius, radius);
        rt.FillRoundedRectangle(ref pillRR, bgBrush);
    }

    /// <summary>
    /// 创建文本格式（统一使用字族、字重、字号；字型与拉伸取 Normal）。
    /// </summary>
    private IDWriteTextFormat CreateTextFormat(string fontFamily, DWriteFontWeight weight, float size)
        => _dwFactory.CreateTextFormat(fontFamily, null!, weight, DWriteFontStyle.Normal, DWriteFontStretch.Normal, size);

    /// <summary>
    /// 创建单行且超出截断的文本布局（NoWrap + 字符级尾随省略号）。
    /// </summary>
    private IDWriteTextLayout CreateTruncatedLayout(string text, string fontFamily, DWriteFontWeight weight, float size, float maxWidth, float maxHeight)
    {
        using var format = CreateTextFormat(fontFamily, weight, size);
        var layout = _dwFactory.CreateTextLayout(text, format, maxWidth, maxHeight);
        layout.WordWrapping = WordWrapping.NoWrap;
        using var ellipsis = _dwFactory.CreateEllipsisTrimmingSign(format);
        layout.SetTrimming(new Trimming { Granularity = TrimmingGranularity.Character, Delimiter = 0, DelimiterCount = 0 }, ellipsis);
        return layout;
    }

    // 媒体文本滚动（跑马灯）参数 —— 与 Notify-Relay-Gamebar 的 Marquee 行为对齐
    private const double MarqueeSpeed = 30.0;        // px/sec
    private const double MarqueeStartDelay = 0.8;   // 起始停留秒数
    private const double MarqueeEndPadding = 12.0;  // 末端留白
    private const float MarqueeMeasureWidth = 10000f;// 测量完整文本宽度的上限

    /// <summary>
    /// 计算跑马灯横向偏移，与 Gamebar 的 TranslateTransform + DoubleAnimationUsingKeyFrames 等价：
    /// 前 MarqueeStartDelay 秒停留原位，随后以 MarqueeSpeed 匀速向左滚动 (overflow + EndPadding) 距离并循环。
    /// </summary>
    private static float ComputeMarqueeOffset(double elapsedSeconds, float overflow)
    {
        if (overflow <= 2) return 0;
        double scrollDistance = overflow + MarqueeEndPadding;
        double duration = Math.Max(scrollDistance / MarqueeSpeed, 1.2);
        double total = MarqueeStartDelay + duration;
        double t = elapsedSeconds % total;
        if (t <= MarqueeStartDelay) return 0;
        double p = (t - MarqueeStartDelay) / duration;
        return -(float)(scrollDistance * p);
    }

    /// <summary>
    /// 绘制媒体文本：播放中且文本宽度超出可用区域时在裁剪框内横向滚动；
    /// 否则在裁剪框内以省略号截断（绝不超出右边界）。
    /// </summary>
    private void DrawMediaMarqueeText(string text, string fontFamily, DWriteFontWeight weight, float size,
        ID2D1DCRenderTarget rt, float x, float y, float availableWidth, float lineHeight,
        MediaCardItem item, double now, double freq, Color4 color)
    {
        if (string.IsNullOrEmpty(text)) return;

        using var fmt = CreateTextFormat(fontFamily, weight, size);

        // 测量完整文本宽度（单行、不换行）
        using var fullLyt = _dwFactory.CreateTextLayout(text, fmt, MarqueeMeasureWidth, lineHeight);
        fullLyt.WordWrapping = WordWrapping.NoWrap;
        float overflow = fullLyt.Metrics.WidthIncludingTrailingWhitespace - availableWidth;

        var clip = new RawRectF(x, y - 2, x + availableWidth, y + lineHeight + 2);
        using var brush = CreateSolidColorBrush(rt, color);

        if (item.IsPlaying && overflow > 2)
        {
            float offset = ComputeMarqueeOffset((now - item.MarqueeAnchorTime) / freq, overflow);
            rt.PushAxisAlignedClip(clip, AntialiasMode.Aliased);
            rt.DrawTextLayout(new Vector2(x + offset, y), fullLyt, brush);
            rt.PopAxisAlignedClip();
        }
        else
        {
            // 非播放或无需滚动：裁剪 + 省略号截断
            rt.PushAxisAlignedClip(clip, AntialiasMode.Aliased);
            using var truncLyt = CreateTruncatedLayout(text, fontFamily, weight, size, availableWidth, lineHeight);
            rt.DrawTextLayout(new Vector2(x, y), truncLyt, brush);
            rt.PopAxisAlignedClip();
        }
    }

    /// <summary>
    /// 以 transform 缩放方式在 (cx, cy) 定位绘制封面位图（保持原 DrawBitmap 调用等价）。
    /// </summary>
    private static void DrawCoverBitmap(ID2D1DCRenderTarget rt, ID2D1Bitmap? bitmap, float cx, float cy, float size, float opacity)
    {
        if (bitmap == null) return;
        var oldTransform = rt.Transform;
        var bmpSize = bitmap.Size;
        float scale = size / Math.Max(bmpSize.Width, bmpSize.Height);
        rt.Transform = Matrix3x2.CreateScale(scale, scale) * Matrix3x2.CreateTranslation(cx, cy);
        rt.DrawBitmap(bitmap, opacity, BitmapInterpolationMode.Linear);
        rt.Transform = oldTransform;
    }

    /// <summary>
    /// 创建纯色画刷（薄封装，便于统一调用）。
    /// </summary>
    private static ID2D1SolidColorBrush CreateSolidColorBrush(ID2D1DCRenderTarget rt, Color4 color)
        => rt.CreateSolidColorBrush(color);
}
