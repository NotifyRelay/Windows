using NotifyRelay.Data.AppDatabase;
using NotifyRelay.Data.Configuration;
using NotifyRelay.Data.Contracts;

namespace NotifyRelay.Services.Settings;

internal sealed class UserSettingsService : IUserSettingsService
{
    private IGeneralSettingsService? _generalSettingsService;

    /// <summary>
    /// Shared configuration root used by all settings services.
    /// </summary>
    public IConfigurationRoot Configuration { get; }

    // Cache for device-specific settings
    private readonly Dictionary<string, IDeviceSettingsService> _deviceSettingsCache = [];

    public UserSettingsService(DatabaseContext dbContext)
    {
        Configuration = new ConfigurationBuilder()
            .Add(new SqliteConfigurationSource(dbContext))
            .Build();
    }

    public IGeneralSettingsService GeneralSettingsService =>
        _generalSettingsService ??= new GeneralSettingsService(Configuration);

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
        var deviceSettings = new DeviceSettingsService(deviceId, Configuration);
        _deviceSettingsCache[deviceId] = deviceSettings;

        return deviceSettings;
    }
}
