namespace NotifyRelay.Services.Overlay;

/// <summary>
/// 键盘状态查询接口，用于叠加层渲染。
/// </summary>
public interface IKeyboardStateProvider
{
    /// <summary>获取当前所有按下的键。</summary>
    IEnumerable<int> GetPressedKeys();

    /// <summary>检查指定键是否按下。</summary>
    bool IsKeyDown(int vkCode);

    /// <summary>检查指定切换键（如 Caps Lock、Num Lock）是否处于开启（锁定）状态。</summary>
    bool IsKeyToggled(int vkCode);

    /// <summary>快捷键映射触发时推送，携带需在叠加层显示的 DisplayText。</summary>
    event EventHandler<KeyMappingDisplayEventArgs>? MappingTriggered;
}

/// <summary>快捷键映射触发事件参数。</summary>
public class KeyMappingDisplayEventArgs : EventArgs
{
    /// <summary>映射配置中的显示文本。</summary>
    public string DisplayText { get; set; } = string.Empty;

    /// <summary>映射的目标键码。</summary>
    public int TargetKey { get; set; }
}
