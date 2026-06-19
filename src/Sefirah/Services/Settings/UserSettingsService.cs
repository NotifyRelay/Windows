using NotifyRelay.Data.AppDatabase;
using NotifyRelay.Data.Contracts;
using NotifyRelay.Utils.Serialization;
using NotifyRelay.Utils.Serialization.Implementation;

namespace NotifyRelay.Services.Settings;

internal sealed class UserSettingsService : BaseJsonSettings, IUserSettingsService
{
    private IGeneralSettingsService _generalSettingsService;
    public IGeneralSettingsService GeneralSettingsService
    {
        get => GetSettingsService(ref _generalSettingsService);
    }

    private readonly DatabaseContext _dbContext;

    // Cache for device-specific settings
    private readonly Dictionary<string, IDeviceSettingsService> _deviceSettingsCache = [];

    public UserSettingsService(DatabaseContext dbContext)
    {
        _dbContext = dbContext;
        JsonSettingsSerializer = new JsonSettingsSerializer();
        JsonSettingsDatabase = new SqliteSettingsDatabase(dbContext, deviceId: null);
        IsAvailable = true;
    }

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
        var deviceSettings = new DeviceSettingsService(deviceId, this, _dbContext);
        _deviceSettingsCache[deviceId] = deviceSettings;

        return deviceSettings;
    }

    private static TSettingsService GetSettingsService<TSettingsService>(ref TSettingsService settingsServiceMember)
        where TSettingsService : class, IBaseSettingsService
    {
        settingsServiceMember ??= Ioc.Default.GetService<TSettingsService>()!;

        return settingsServiceMember;
    }
}
