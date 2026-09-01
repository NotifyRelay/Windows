using System.Collections.Concurrent;
using System.Linq;
using System.Runtime.InteropServices;
using NotifyRelay.Data.Contracts;
using NotifyRelay.Data.Models.Actions;
using NotifyRelay.Services.Overlay;

namespace NotifyRelay.Platforms.Windows.Services;

/// <summary>
/// 键盘钩子服务：低级键盘钩子捕获按键事件，支持快捷键映射和按键状态追踪。
/// </summary>
public sealed class KeyboardHookService : IKeyboardStateProvider, IDisposable
{
    private readonly ILogger<KeyboardHookService> _logger;
    private readonly IGeneralSettingsService _settings;

    private IntPtr _hookId = IntPtr.Zero;
    private NativeMethods.LowLevelKeyboardProc? _hookProc;
    private bool _disposed;

    // 按键状态字典：Key → 是否按下
    private readonly ConcurrentDictionary<int, bool> _keyStates = new();

    // 切换键（锁定键）状态字典：Key → 是否开启（如 Caps Lock / Num Lock / Scroll Lock）
    private readonly ConcurrentDictionary<int, bool> _toggleStates = new();

    // 切换键（锁定键）虚拟键码集合
    private static readonly HashSet<int> ToggleKeys = new() { 0x14, 0x90, 0x91 };

    // 映射中的源键组合状态：映射ID → 按下的源键集合
    private readonly ConcurrentDictionary<string, HashSet<int>> _mappingKeyStates = new();

    // 活跃的映射：源键组合（排序后）→ 映射配置
    private Dictionary<string, KeyboardMappingConfig> _activeMappings = new();

    // 按键状态变化事件
    public event EventHandler<KeyStateChangedEventArgs>? KeyStateChanged;

    // 快捷键映射触发事件：推送 DisplayText 供叠加层显示
    public event EventHandler<KeyMappingDisplayEventArgs>? MappingTriggered;

    /// <summary>当前所有按键状态的快照。</summary>
    public IReadOnlyDictionary<int, bool> KeyStates => _keyStates;

    public KeyboardHookService(ILogger<KeyboardHookService> logger, IGeneralSettingsService settings)
    {
        _logger = logger;
        _settings = settings;
        _hookProc = HookCallback;
        ReloadMappings();
    }

    /// <summary>重新加载快捷键映射配置。</summary>
    public void ReloadMappings()
    {
        var mappings = _settings.KeyboardMappings;
        _activeMappings = mappings
            .Where(m => m.Enabled)
            .ToDictionary(
                m => string.Join("+", m.SourceKeys.OrderBy(k => k)),
                m => m
            );
        _logger.LogDebug("加载了 {Count} 个快捷键映射", _activeMappings.Count);
    }

    /// <summary>安装键盘钩子。</summary>
    public void Install()
    {
        if (_hookId != IntPtr.Zero) return;

        using var module = System.Diagnostics.Process.GetCurrentProcess().MainModule;
        if (module?.BaseAddress == null)
        {
            _logger.LogWarning("键盘钩子安装失败：无法获取主模块句柄");
            return;
        }

        // 安装前先读取切换键的初始锁定状态
        foreach (var vk in ToggleKeys)
            _toggleStates[vk] = (NativeMethods.GetKeyState(vk) & 0x0001) != 0;

        _hookId = NativeMethods.SetWindowsHookEx(
            NativeMethods.WH_KEYBOARD_LL,
            _hookProc,
            NativeMethods.GetModuleHandle(module.ModuleName),
            0);

        if (_hookId == IntPtr.Zero)
        {
            var error = Marshal.GetLastWin32Error();
            _logger.LogError("安装键盘钩子失败，错误码: {Error}", error);
        }
        else
        {
            _logger.LogInformation("键盘钩子已安装");
        }
    }

    /// <summary>卸载键盘钩子。</summary>
    public void Uninstall()
    {
        if (_hookId == IntPtr.Zero) return;

        if (NativeMethods.UnhookWindowsHookEx(_hookId))
        {
            _logger.LogInformation("键盘钩子已卸载");
        }
        _hookId = IntPtr.Zero;
    }

    /// <summary>按当前开关状态同步钩子安装/卸载，供启动与开关切换复用。</summary>
    public void SyncState()
    {
        if (_settings.KeyboardOverlayEnabled)
            Install();
        else
            Uninstall();
    }

    private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0)
        {
            var hookStruct = Marshal.PtrToStructure<NativeMethods.KBDLLHOOKSTRUCT>(lParam);
            int vkCode = (int)hookStruct.vkCode;
            bool isKeyUp = wParam == NativeMethods.WM_KEYUP || wParam == NativeMethods.WM_SYSKEYUP;
            bool isKeyDown = !isKeyUp;

            // 更新按键状态
            _keyStates[vkCode] = isKeyDown;

            // 切换键（Caps/Num/Scroll Lock）在按下时翻转锁定状态
            if (isKeyDown && ToggleKeys.Contains(vkCode))
            {
                _toggleStates.AddOrUpdate(vkCode, _ => true, (_, old) => !old);
            }

            // 触发状态变化事件
            KeyStateChanged?.Invoke(this, new KeyStateChangedEventArgs
            {
                Key = vkCode,
                IsPressed = isKeyDown,
                AllStates = _keyStates.ToDictionary(kvp => kvp.Key, kvp => kvp.Value)
            });

            // 处理快捷键映射
            if (HandleKeyMapping(vkCode, isKeyDown))
            {
                return (IntPtr)1; // 吞掉事件
            }
        }

        return NativeMethods.CallNextHookEx(_hookId, nCode, wParam, lParam);
    }

    private bool HandleKeyMapping(int vkCode, bool isKeyDown)
    {
        var mappings = _settings.KeyboardMappings.Where(m => m.Enabled).ToList();

        foreach (var mapping in mappings)
        {
            // 检查源键组合
            if (!mapping.SourceKeys.Contains(vkCode)) continue;

            // 获取或创建该映射的按键状态跟踪
            var stateKey = mapping.Id ?? string.Join(",", mapping.SourceKeys);
            var pressedKeys = _mappingKeyStates.GetOrAdd(stateKey, _ => new HashSet<int>());

            if (isKeyDown)
            {
                pressedKeys.Add(vkCode);
            }
            else
            {
                pressedKeys.Remove(vkCode);
            }

            // 检查是否所有源键都按下
            bool allSourceKeysPressed = mapping.SourceKeys.All(k => pressedKeys.Contains(k));

            if (allSourceKeysPressed && isKeyDown)
            {
                // 执行映射：发送目标键
                SendKeyPress(mapping.TargetKey);

                // 通知叠加层显示 DisplayText（若有）
                if (!string.IsNullOrEmpty(mapping.DisplayText))
                {
                    MappingTriggered?.Invoke(this, new KeyMappingDisplayEventArgs
                    {
                        DisplayText = mapping.DisplayText,
                        TargetKey = mapping.TargetKey
                    });
                }

                _logger.LogDebug("快捷键映射触发: {Source} → {Target}",
                    string.Join("+", mapping.SourceKeys.Select(k => GetKeyName(k))),
                    GetKeyName(mapping.TargetKey));

                return true; // 吞掉源键事件
            }
        }

        return false;
    }

    /// <summary>模拟按键发送。</summary>
    private void SendKeyPress(int virtualKey)
    {
        var inputs = new NativeMethods.INPUT[2];

        // 按下
        inputs[0].type = NativeMethods.INPUT_KEYBOARD;
        inputs[0].ki.ki.wVk = (ushort)virtualKey;
        inputs[0].ki.ki.dwFlags = 0;

        // 释放
        inputs[1].type = NativeMethods.INPUT_KEYBOARD;
        inputs[1].ki.ki.wVk = (ushort)virtualKey;
        inputs[1].ki.ki.dwFlags = NativeMethods.KEYEVENTF_KEYUP;

        NativeMethods.SendInput(2, inputs, Marshal.SizeOf<NativeMethods.INPUT>());
    }

    /// <summary>检查指定键码是否按下。</summary>
    public bool IsKeyDown(int vkCode)
    {
        return _keyStates.TryGetValue(vkCode, out bool isDown) && isDown;
    }

    /// <summary>检查指定切换键（如 Caps Lock）是否处于开启（锁定）状态。</summary>
    public bool IsKeyToggled(int vkCode)
    {
        return _toggleStates.TryGetValue(vkCode, out bool toggled) && toggled;
    }

    /// <summary>获取当前所有按下的键。</summary>
    public IEnumerable<int> GetPressedKeys()
    {
        return _keyStates.Where(kvp => kvp.Value).Select(kvp => kvp.Key);
    }

    private static string GetKeyName(int vkCode)
    {
        return vkCode switch
        {
            0x08 => "Backspace",
            0x09 => "Tab",
            0x0D => "Enter",
            0x10 => "Shift",
            0x11 => "Ctrl",
            0x12 => "Alt",
            0x14 => "CapsLock",
            0x1B => "Escape",
            0x20 => "Space",
            >= 0x30 and <= 0x39 => ((char)vkCode).ToString(),
            >= 0x41 and <= 0x5A => ((char)vkCode).ToString(),
            >= 0x70 and <= 0x87 => $"F{vkCode - 0x6F}",
            0xA0 => "LShift",
            0xA1 => "RShift",
            0xA2 => "LCtrl",
            0xA3 => "RCtrl",
            0xA4 => "LAlt",
            0xA5 => "RAlt",
            _ => $"0x{vkCode:X2}"
        };
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Uninstall();
    }
}

/// <summary>按键状态变化事件参数。</summary>
public class KeyStateChangedEventArgs : EventArgs
{
    public int Key { get; set; }
    public bool IsPressed { get; set; }
    public IReadOnlyDictionary<int, bool> AllStates { get; set; } = new Dictionary<int, bool>();
}

/// <summary>快捷键映射配置。</summary>
public class KeyboardMappingConfig
{
    public string? Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public List<int> SourceKeys { get; set; } = new();
    public int TargetKey { get; set; }
    public string? DisplayText { get; set; }
    public bool Enabled { get; set; } = true;

    /// <summary>获取映射的显示文本。</summary>
    public string GetMappingDisplay()
    {
        var source = string.Join("+", SourceKeys.Select(k => GetKeyName(k)));
        if (TargetKey > 0)
        {
            var target = GetKeyName(TargetKey);
            var display = string.IsNullOrEmpty(DisplayText) ? target : $"{target} ({DisplayText})";
            return $"{source} → {display}";
        }
        return string.IsNullOrEmpty(DisplayText) ? source : $"{source} ({DisplayText})";
    }

    public static string GetKeyName(int vkCode)
    {
        return vkCode switch
        {
            0x08 => "Backspace",
            0x09 => "Tab",
            0x0D => "Enter",
            0x10 => "Shift",
            0x11 => "Ctrl",
            0x12 => "Alt",
            0x14 => "CapsLock",
            0x1B => "Escape",
            0x20 => "Space",
            >= 0x30 and <= 0x39 => ((char)vkCode).ToString(),
            >= 0x41 and <= 0x5A => ((char)vkCode).ToString(),
            >= 0x70 and <= 0x87 => $"F{vkCode - 0x6F}",
            0xA0 => "LShift",
            0xA1 => "RShift",
            0xA2 => "LCtrl",
            0xA3 => "RCtrl",
            0xA4 => "LAlt",
            0xA5 => "RAlt",
            _ => $"0x{vkCode:X2}"
        };
    }
}
