namespace NotifyRelay.Services.Overlay;

/// <summary>
/// 叠加层元素（心率 / 键盘 / 罗技电池 / 后续新增元素）共用的核心方法。
/// 目标：新增元素时直接复用，而不是复制一份近似的判定与定位实现。
/// </summary>
internal static class OverlayElementCore
{
    /// <summary>
    /// 判断指定覆盖层窗口是否为元素的目标屏。
    /// 目标取值：primary（主屏，忽略大小写） / span（跨屏窗口，仅 <paramref name="allowSpan"/> 时有效） /
    /// 具体显示器 DeviceName（如 \\.\DISPLAY2，按 <see cref="StringComparison.Ordinal"/> 精确匹配）。
    /// 匹配不到时回退主屏，跨屏窗口不参与 DeviceName 匹配。
    /// </summary>
    /// <param name="o">待判定的覆盖层窗口。</param>
    /// <param name="target">目标屏取值。</param>
    /// <param name="overlays">当前所有覆盖层窗口。</param>
    /// <param name="spanOverlay">跨屏窗口（跨屏模式下存在）。</param>
    /// <param name="allowSpan">是否允许命中跨屏窗口（心率等不支持跨屏的元素传 false）。</param>
    /// <remarks>每帧热路径调用：仅 for 遍历 + 字符串比较，不产生委托或集合分配。</remarks>
    public static bool IsTargetScreen(ScreenOverlay o, string? target,
        IReadOnlyList<ScreenOverlay> overlays, ScreenOverlay? spanOverlay, bool allowSpan)
    {
        if (string.IsNullOrEmpty(target)) return o.IsPrimary;
        if (string.Equals(target, "primary", StringComparison.OrdinalIgnoreCase)) return o.IsPrimary;
        if (allowSpan && string.Equals(target, "span", StringComparison.OrdinalIgnoreCase))
            return spanOverlay != null && ReferenceEquals(o, spanOverlay);

        for (int i = 0; i < overlays.Count; i++)
        {
            var x = overlays[i];
            if (x.IsSpan) continue;
            if (string.Equals(x.DeviceName, target, StringComparison.Ordinal))
                return ReferenceEquals(o, x);
        }
        return o.IsPrimary;
    }

    /// <summary>
    /// 按百分比解析元素锚点坐标（元素左上角/中心基准由调用方决定）。
    /// 百分比统一钳制到 0~100，避免越界设置把元素推到屏幕外。
    /// </summary>
    public static (float X, float Y) ResolveAnchor(ScreenOverlay o, float xPercent, float yPercent)
        => (o.Width * Math.Clamp(xPercent, 0f, 100f) / 100f,
            o.Height * Math.Clamp(yPercent, 0f, 100f) / 100f);

    /// <summary>解析元素整体缩放系数（各元素量程不同，上下限由调用方传入）。</summary>
    public static float ResolveScale(float rawScale, float min, float max)
        => Math.Clamp(rawScale, min, max);

    /// <summary>
    /// 替换元素的数据源（Provider）并同步事件订阅：先退订旧的，再订阅新的。
    /// 抽取了键盘 / 罗技电池各自重复的"退订—赋值—订阅"模板，后续新增元素直接复用。
    /// </summary>
    /// <typeparam name="TProvider">数据源类型。</typeparam>
    /// <param name="current">当前数据源字段（以 ref 传入，方法内完成赋值）。</param>
    /// <param name="next">新的数据源，传 null 表示清空。</param>
    /// <param name="subscribe">订阅回调。</param>
    /// <param name="unsubscribe">退订回调。</param>
    public static void ReplaceProvider<TProvider>(ref TProvider? current, TProvider? next,
        Action<TProvider> subscribe, Action<TProvider> unsubscribe) where TProvider : class
    {
        if (current != null) unsubscribe(current);
        current = next;
        if (next != null) subscribe(next);
    }
}
