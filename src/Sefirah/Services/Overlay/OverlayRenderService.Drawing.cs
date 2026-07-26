using System;
using System.Numerics;
using System.Drawing;
using Vortice;
using Vortice.Direct2D1;
using Vortice.DirectWrite;
using Vortice.Mathematics;
using DWriteFontWeight = Vortice.DirectWrite.FontWeight;
using DWriteFontStyle = Vortice.DirectWrite.FontStyle;
using DWriteFontStretch = Vortice.DirectWrite.FontStretch;
using BitmapInterpolationMode = Vortice.Direct2D1.BitmapInterpolationMode;

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
        => _dwFactory.CreateTextFormat(fontFamily, null, weight, DWriteFontStyle.Normal, DWriteFontStretch.Normal, size);

    /// <summary>
    /// 创建单行且超出截断的文本布局（NoWrap + 尾随省略号）。
    /// </summary>
    private IDWriteTextLayout CreateTruncatedLayout(string text, string fontFamily, DWriteFontWeight weight, float size, float maxWidth, float maxHeight)
    {
        using var format = CreateTextFormat(fontFamily, weight, size);
        var layout = _dwFactory.CreateTextLayout(text, format, maxWidth, maxHeight);
        layout.WordWrapping = WordWrapping.NoWrap;
        layout.SetTrimming(new Trimming { Delimiter = 0, DelimiterCount = 0 }, null!);
        return layout;
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
