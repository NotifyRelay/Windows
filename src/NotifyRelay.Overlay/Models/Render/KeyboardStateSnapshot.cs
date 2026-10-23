namespace NotifyRelay.Models.Render;

/// <summary>键盘状态快照：按下的虚拟键码集合 + 三个切换键的锁定状态。</summary>
public sealed class KeyboardStateSnapshot
{
    /// <summary>当前按下的虚拟键码。</summary>
    public int[] PressedKeys { get; init; } = [];

    /// <summary>Caps Lock（0x14）是否锁定。</summary>
    public bool CapsLockToggled { get; init; }

    /// <summary>Num Lock（0x90）是否锁定。</summary>
    public bool NumLockToggled { get; init; }

    /// <summary>Scroll Lock（0x91）是否锁定。</summary>
    public bool ScrollLockToggled { get; init; }

    /// <summary>空快照（无按键、切换键均未锁定）。</summary>
    public static KeyboardStateSnapshot Empty { get; } = new();

    /// <summary>读取指定切换键的锁定状态；非切换键返回 false。</summary>
    public bool IsToggled(int vkCode) => vkCode switch
    {
        0x14 => CapsLockToggled,
        0x90 => NumLockToggled,
        0x91 => ScrollLockToggled,
        _ => false,
    };
}
