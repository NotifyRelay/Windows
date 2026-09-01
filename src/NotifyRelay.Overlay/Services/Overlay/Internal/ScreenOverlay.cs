using NotifyRelay.Models.Render;
using Vortice.Direct2D1;

namespace NotifyRelay.Services.Overlay;

/// <summary>单个屏幕（或跨屏）的覆盖层窗口及其资源。仅由渲染线程创建、访问与销毁。</summary>
internal sealed class ScreenOverlay
{
    public IntPtr Hwnd;
    public IntPtr MemDC;
    public IntPtr HBitmap;
    public IntPtr OldBitmap;
    public ID2D1DCRenderTarget? RenderTarget;
    public int OriginX;
    public int OriginY;
    public int Width;
    public int Height;
    public bool IsPrimary;
    public bool IsSpan;
    public bool Visible;
    public string DeviceName = string.Empty;
    public float TopOffset;   // 主屏顶部卡片占用高度，用于弹幕轨道起点
    public readonly List<DanmakuItem> Items = [];
    public readonly Queue<DanmakuItem> Pending = new();
}

/// <summary>显示器几何信息（枚举结果）。</summary>
internal struct ScreenInfo
{
    public string DeviceName;
    public int X, Y, W, H;
    public bool IsPrimary;
}
