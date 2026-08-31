using System.Collections.ObjectModel;
using System.Runtime.CompilerServices;
using NotifyRelay.Data.Contracts;
using NotifyRelay.Platforms.Windows.Services;

namespace NotifyRelay.ViewModels.Settings;

public class KeyboardViewModel : INotifyPropertyChanged
{
    private readonly IGeneralSettingsService _settings;
    private readonly KeyboardHookService? _keyboardHookService;

    public event PropertyChangedEventHandler? PropertyChanged;

    public bool KeyboardOverlayEnabled
    {
        get => _settings.KeyboardOverlayEnabled;
        set
        {
            _settings.KeyboardOverlayEnabled = value;
            OnPropertyChanged();
            UpdateHookService();
        }
    }

    public ObservableCollection<KeyboardMappingConfig> Mappings { get; } = new();

    public KeyboardViewModel()
    {
        _settings = Ioc.Default.GetRequiredService<IGeneralSettingsService>();
        _keyboardHookService = Ioc.Default.GetService<KeyboardHookService>();

        // 加载现有映射
        foreach (var mapping in _settings.KeyboardMappings)
        {
            Mappings.Add(mapping);
        }
    }

    private void UpdateHookService()
    {
        if (_keyboardHookService == null) return;

        if (KeyboardOverlayEnabled)
        {
            _keyboardHookService.Install();
        }
        else
        {
            _keyboardHookService.Uninstall();
        }
    }

    public void AddMapping(KeyboardMappingConfig mapping)
    {
        mapping.Id = Guid.NewGuid().ToString("N")[..8];
        Mappings.Add(mapping);
        SaveMappings();
    }

    public void UpdateMapping(KeyboardMappingConfig mapping)
    {
        var index = Mappings.ToList().FindIndex(m => m.Id == mapping.Id);
        if (index >= 0)
        {
            Mappings[index] = mapping;
            SaveMappings();
        }
    }

    public void RemoveMapping(KeyboardMappingConfig mapping)
    {
        Mappings.Remove(mapping);
        SaveMappings();
    }

    private void SaveMappings()
    {
        _settings.KeyboardMappings = Mappings.ToList();
        _keyboardHookService?.ReloadMappings();
    }

    protected void OnPropertyChanged([CallerMemberName] string propertyName = "")
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    /// <summary>获取所有可用的虚拟键码。</summary>
    public static List<KeyOption> GetAvailableKeys()
    {
        return new List<KeyOption>
        {
            new(0x08, "Backspace"),
            new(0x09, "Tab"),
            new(0x0D, "Enter"),
            new(0x10, "Shift"),
            new(0x11, "Ctrl"),
            new(0x12, "Alt"),
            new(0x13, "Pause"),
            new(0x14, "CapsLock"),
            new(0x1B, "Escape"),
            new(0x20, "Space"),
            new(0x21, "PageUp"),
            new(0x22, "PageDown"),
            new(0x23, "End"),
            new(0x24, "Home"),
            new(0x25, "Left"),
            new(0x26, "Up"),
            new(0x27, "Right"),
            new(0x28, "Down"),
            new(0x30, "0"),
            new(0x31, "1"),
            new(0x32, "2"),
            new(0x33, "3"),
            new(0x34, "4"),
            new(0x35, "5"),
            new(0x36, "6"),
            new(0x37, "7"),
            new(0x38, "8"),
            new(0x39, "9"),
            new(0x41, "A"),
            new(0x42, "B"),
            new(0x43, "C"),
            new(0x44, "D"),
            new(0x45, "E"),
            new(0x46, "F"),
            new(0x47, "G"),
            new(0x48, "H"),
            new(0x49, "I"),
            new(0x4A, "J"),
            new(0x4B, "K"),
            new(0x4C, "L"),
            new(0x4D, "M"),
            new(0x4E, "N"),
            new(0x4F, "O"),
            new(0x50, "P"),
            new(0x51, "Q"),
            new(0x52, "R"),
            new(0x53, "S"),
            new(0x54, "T"),
            new(0x55, "U"),
            new(0x56, "V"),
            new(0x57, "W"),
            new(0x58, "X"),
            new(0x59, "Y"),
            new(0x5A, "Z"),
            new(0x60, "NumPad0"),
            new(0x61, "NumPad1"),
            new(0x62, "NumPad2"),
            new(0x63, "NumPad3"),
            new(0x64, "NumPad4"),
            new(0x65, "NumPad5"),
            new(0x66, "NumPad6"),
            new(0x67, "NumPad7"),
            new(0x68, "NumPad8"),
            new(0x69, "NumPad9"),
            new(0x70, "F1"),
            new(0x71, "F2"),
            new(0x72, "F3"),
            new(0x73, "F4"),
            new(0x74, "F5"),
            new(0x75, "F6"),
            new(0x76, "F7"),
            new(0x77, "F8"),
            new(0x78, "F9"),
            new(0x79, "F10"),
            new(0x7A, "F11"),
            new(0x7B, "F12"),
            new(0x90, "NumLock"),
            new(0xA0, "LShift"),
            new(0xA1, "RShift"),
            new(0xA2, "LCtrl"),
            new(0xA3, "RCtrl"),
            new(0xA4, "LAlt"),
            new(0xA5, "RAlt"),
        };
    }
}

public record KeyOption(int VkCode, string DisplayName);
