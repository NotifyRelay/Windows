using System.Text.Json;
using Microsoft.Extensions.Configuration;
using NotifyRelay.Data.AppDatabase;
using NotifyRelay.Data.AppDatabase.Models;

namespace NotifyRelay.Data.Configuration;

/// <summary>
/// Configuration source backed by the application's SQLite settings table.
/// Keys are stored as "General:{name}" for global settings and
/// "Devices:{deviceId}:{name}" for per-device settings.
/// </summary>
public sealed class SqliteConfigurationSource(DatabaseContext dbContext) : IConfigurationSource
{
    public IConfigurationProvider Build(IConfigurationBuilder builder) =>
        new SqliteConfigurationProvider(dbContext);
}

/// <summary>
/// Configuration provider that reads and writes settings from the AppSettingEntity table.
/// </summary>
public sealed class SqliteConfigurationProvider : ConfigurationProvider
{
    private const string GeneralPrefix = "General:";
    private const string DevicesPrefix = "Devices:";
    private const char DeviceIdSeparator = ':';
    private const char EncodedSeparator = '\u0001';

    private readonly DatabaseContext _dbContext;

    public SqliteConfigurationProvider(DatabaseContext dbContext)
    {
        _dbContext = dbContext;
    }

    public override void Load()
    {
        var data = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);

        foreach (var entity in _dbContext.Database.Table<AppSettingEntity>().ToList())
        {
            var key = entity.DeviceId is null
                ? GeneralPrefix + entity.Key
                : DevicesPrefix + entity.DeviceId.Replace(':', EncodedSeparator) + DeviceIdSeparator + entity.Key;
            data[key] = entity.Value;
        }

        Data = data;
    }

    public override void Set(string key, string? value)
    {
        base.Set(key, value);

        if (TryParseKey(key, out var deviceId, out var settingKey))
        {
            Persist(deviceId, settingKey, value);
        }
    }

    private static bool TryParseKey(string key, out string? deviceId, out string settingKey)
    {
        deviceId = null;
        settingKey = string.Empty;

        if (key.StartsWith(GeneralPrefix, StringComparison.OrdinalIgnoreCase))
        {
            settingKey = key[GeneralPrefix.Length..];
            return true;
        }

        if (key.StartsWith(DevicesPrefix, StringComparison.OrdinalIgnoreCase))
        {
            var rest = key[DevicesPrefix.Length..];
            var separatorIndex = rest.LastIndexOf(DeviceIdSeparator);
            if (separatorIndex < 0)
                return false;

            var encodedDeviceId = rest[..separatorIndex];
            deviceId = encodedDeviceId.Replace(EncodedSeparator, ':');
            settingKey = rest[(separatorIndex + 1)..];
            return true;
        }

        return false;
    }

    private void Persist(string? deviceId, string settingKey, string? value)
    {
        var db = _dbContext.Database;
        var existing = db.Table<AppSettingEntity>()
            .Where(e => e.Key == settingKey && e.DeviceId == deviceId)
            .FirstOrDefault();

        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        if (existing is not null)
        {
            existing.Value = value;
            existing.UpdatedAt = now;
            db.Update(existing);
        }
        else
        {
            db.Insert(new AppSettingEntity
            {
                Key = settingKey,
                Value = value,
                DeviceId = deviceId,
                UpdatedAt = now
            });
        }
    }

    /// <summary>
    /// Builds the configuration key for a setting.
    /// </summary>
    public static string BuildKey(string? deviceId, string settingName)
    {
        return deviceId is null
            ? GeneralPrefix + settingName
            : DevicesPrefix + deviceId.Replace(':', EncodedSeparator) + DeviceIdSeparator + settingName;
    }
}
