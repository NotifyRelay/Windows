namespace NotifyRelay.Services.Overlay.UI;

/// <summary>
/// 叠加层元素契约：一个元素 = 一份数据状态 + 一棵声明式 UI 子树。
/// 元素不再自行调用 D2D 绘制，也不再自行解析目标屏 / 释放字段级资源。
/// </summary>
internal interface IOverlayElement
{
    /// <summary>元素名（日志用）。</summary>
    string Name { get; }

    /// <summary>从已保存设置加载初始配置（Start 时统一调用）。</summary>
    void LoadSettings(IOverlaySettings settings);

    /// <summary>元素是否全局活跃（决定渲染循环是否需要保持运行）。</summary>
    bool IsActive();

    /// <summary>本元素是否需要在本窗口上绘制（目标屏解析 + 元素自身显示条件）。</summary>
    bool IsTargetScreen(ScreenOverlay o);

    /// <summary>声明本元素在当前窗口上的 UI 子树。</summary>
    void Compose(OverlayComposer composer, ScreenOverlay o);

    /// <summary>窗口失效 / 覆盖层重建时释放与渲染目标相关或缓存的资源（渲染线程调用）。</summary>
    void Reset();
}
