using NotifyRelay.Data.AppDatabase.Repository;
using NotifyRelay.Data.Contracts;

namespace NotifyRelay.Services.Settings;

internal sealed class UserSettingsService : IUserSettingsService
{
    private IGeneralSettingsService? _generalSettingsService;

    /// <summary>
    /// Shared strongly-typed settings store, backed by the SQLite <c>AppSettingEntity</c> table.
    /// </summary>
    public SettingsRepository SettingsRepository { get; }

    // Cache for device-specific settings
    private readonly Dictionary<string, IDeviceSettingsService> _deviceSettingsCache = [];

    public UserSettingsService(SettingsRepository settingsRepository)
    {
        SettingsRepository = settingsRepository;
    }

    public IGeneralSettingsService GeneralSettingsService =>
        _generalSettingsService ??= new GeneralSettingsService(SettingsRepository);

    public IDeviceSettingsService GetDeviceSettings(string deviceId)
    {
        if (string.IsNullOrWhiteSpace(deviceId))
            throw new ArgumentException("Device ID cannot be null or whitespace", nameof(deviceId));

        // Return cached instance if available
        if (_deviceSettingsCache.TryGetValue(deviceId, out var cachedSettings))
        {
            return cachedSettings;
        }

        // Create new device-specific settings instance
        var deviceSettings = new DeviceSettingsService(deviceId, SettingsRepository);
        _deviceSettingsCache[deviceId] = deviceSettings;

        return deviceSettings;
    }
}
