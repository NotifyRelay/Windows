namespace NotifyRelay.Services.Overlay;

/// <summary>
/// 显示器枚举实现：直接复用覆盖层窗口管理器的枚举结果，
/// 与覆盖层每屏窗口的 DeviceName / IsPrimary 取值同源。
/// </summary>
public sealed class OverlayScreenCatalog : IScreenCatalog
{
    /// <summary>枚举当前所有显示器。</summary>
    public IReadOnlyList<ScreenDescriptor> GetScreens()
    {
        var screens = OverlayWindowManager.EnumerateScreens();
        var result = new List<ScreenDescriptor>(screens.Count);
        foreach (var s in screens)
            result.Add(new ScreenDescriptor(s.DeviceName, s.IsPrimary));
        return result;
    }
}
