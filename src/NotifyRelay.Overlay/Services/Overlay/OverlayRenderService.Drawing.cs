using Vortice.Direct2D1;
using Vortice.DirectWrite;
using Vortice.Mathematics;
using DWriteFontStretch = Vortice.DirectWrite.FontStretch;
using DWriteFontStyle = Vortice.DirectWrite.FontStyle;
using DWriteFontWeight = Vortice.DirectWrite.FontWeight;

namespace NotifyRelay.Services.Overlay;

/// <summary>
/// OverlayRenderService 的命令式绘制辅助（弹幕与位图加载路径共用）。
/// 声明式节点的绘制入口在 UI/PaintScope.cs，此处只保留仍需服务直接调用的部分。
/// </summary>
public partial class OverlayRenderService
{
    /// <summary>创建文本格式（统一使用字族、字重、字号；字型与拉伸取 Normal）。</summary>
    private IDWriteTextFormat CreateTextFormat(string fontFamily, DWriteFontWeight weight, float size)
        => _dwFactory.CreateTextFormat(fontFamily, null!, weight, DWriteFontStyle.Normal,
            DWriteFontStretch.Normal, size);

    /// <summary>创建纯色画刷（薄封装，便于统一调用；弹幕路径逐帧创建并随用随放）。</summary>
    private static ID2D1SolidColorBrush CreateSolidColorBrush(ID2D1DCRenderTarget rt, Color4 color)
        => rt.CreateSolidColorBrush(color);
}
