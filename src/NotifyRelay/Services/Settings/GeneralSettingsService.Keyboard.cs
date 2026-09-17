using NotifyRelay.Data.Configuration;
using NotifyRelay.Platforms.Windows.Services;

namespace NotifyRelay.Services.Settings;

internal sealed partial class GeneralSettingsService
{
    // 键盘叠加层设置
    public bool KeyboardOverlayEnabled
    {
        get => _configuration.Get(SettingsKey(nameof(KeyboardOverlayEnabled)), false);
        set => _configuration.Set(SettingsKey(nameof(KeyboardOverlayEnabled)), value);
    }

    public List<KeyboardMappingConfig> KeyboardMappings
    {
        get => _configuration.Get(SettingsKey(nameof(KeyboardMappings)), new List<KeyboardMappingConfig>())!;
        set => _configuration.Set(SettingsKey(nameof(KeyboardMappings)), value);
    }
}
