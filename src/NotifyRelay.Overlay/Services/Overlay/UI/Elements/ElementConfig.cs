namespace NotifyRelay.Services.Overlay.UI.Elements;

/// <summary>Overlay 元素 SetConfig 的共用夹取入口。</summary>
internal static class ElementConfig
{
    /// <summary>位置百分比（X 轴）夹取。</summary>
    public static float ClampXPercent(float xPct) => Math.Clamp(xPct, 0f, 100f);

    /// <summary>位置百分比（Y 轴）夹取。</summary>
    public static float ClampYPercent(float yPct) => Math.Clamp(yPct, 0f, 100f);

    /// <summary>文本描边宽度夹取。</summary>
    public static float ClampOutlineWidth(float outlineWidth) => Math.Clamp(outlineWidth, 0.1f, 3f);

    /// <summary>元素缩放系数夹取。</summary>
    public static float ClampScale(float scale) => Math.Clamp(scale, 0.5f, 2f);
}
