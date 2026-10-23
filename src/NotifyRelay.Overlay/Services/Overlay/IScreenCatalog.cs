namespace NotifyRelay.Services.Overlay;

/// <summary>显示器描述（DeviceName + 是否主屏）。</summary>
public readonly record struct ScreenDescriptor(string DeviceName, bool IsPrimary);

/// <summary>
/// 显示器枚举契约：供设置页构建目标屏下拉选项，不依赖渲染服务实例。
/// </summary>
public interface IScreenCatalog
{
    /// <summary>枚举当前所有显示器。</summary>
    IReadOnlyList<ScreenDescriptor> GetScreens();
}
