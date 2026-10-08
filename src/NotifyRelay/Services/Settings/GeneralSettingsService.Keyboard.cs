using NotifyRelay.Platforms.Windows.Services;

namespace NotifyRelay.Services.Settings;

internal sealed partial class GeneralSettingsService
{
    // 键盘叠加层设置
    public bool KeyboardOverlayEnabled
    {
        get => _settings.Get(nameof(KeyboardOverlayEnabled), false);
        set => _settings.Set(nameof(KeyboardOverlayEnabled), value);
    }

    public List<KeyboardMappingConfig> KeyboardMappings
    {
        get => _settings.Get(nameof(KeyboardMappings), new List<KeyboardMappingConfig>());
        set => _settings.Set(nameof(KeyboardMappings), value);
    }
}
