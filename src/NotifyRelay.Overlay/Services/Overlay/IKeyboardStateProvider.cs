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
}
