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
}

public record KeyOption(int VkCode, string DisplayName);
