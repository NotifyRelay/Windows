using System.Collections.Concurrent;
using NotifyRelay.Data.AppDatabase;
using NotifyRelay.Data.AppDatabase.Models;

namespace NotifyRelay.Utils.Serialization.Implementation;

internal sealed class SqliteSettingsDatabase : IJsonSettingsDatabase
{
    private readonly DatabaseContext _dbContext;
    private readonly string? _deviceId;
    private readonly IJsonSettingsSerializer _serializer = new JsonSettingsSerializer();

    private readonly ConcurrentDictionary<string, string?> _cache = new();

    public SqliteSettingsDatabase(DatabaseContext dbContext, string? deviceId = null)
    {
        _dbContext = dbContext;
        _deviceId = deviceId;
        LoadCache();
    }

    private void LoadCache()
    {
        var entities = _dbContext.Database
            .Table<AppSettingEntity>()
            .Where(e => e.DeviceId == _deviceId)
            .ToList();

        foreach (var entity in entities)
        {
            _cache[entity.Key] = entity.Value;
        }
    }

    public TValue? GetValue<TValue>(string key, TValue? defaultValue = default)
    {
        if (_cache.TryGetValue(key, out var cachedValue))
        {
            if (cachedValue == null)
                return defaultValue;
            return _serializer.DeserializeFromJson<TValue>(cachedValue) ?? defaultValue;
        }

        var entity = _dbContext.Database
            .Table<AppSettingEntity>()
            .FirstOrDefault(e => e.Key == key && e.DeviceId == _deviceId);

        if (entity != null)
        {
            _cache[key] = entity.Value;
            if (entity.Value == null)
                return defaultValue;
            return _serializer.DeserializeFromJson<TValue>(entity.Value) ?? defaultValue;
        }

        SetValue(key, defaultValue);
        return defaultValue;
    }

    public bool SetValue<TValue>(string key, TValue? newValue)
    {
        var valueStr = newValue == null ? null : _serializer.SerializeToJson(newValue);

        var existing = _dbContext.Database
            .Table<AppSettingEntity>()
            .FirstOrDefault(e => e.Key == key && e.DeviceId == _deviceId);

        if (existing != null)
        {
            existing.Value = valueStr;
            existing.UpdatedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            _dbContext.Database.Update(existing);
        }
        else
        {
            _dbContext.Database.Insert(new AppSettingEntity
            {
                Key = key,
                Value = valueStr,
                DeviceId = _deviceId,
                UpdatedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
            });
        }

        _cache[key] = valueStr;
        return true;
    }

    public bool RemoveKey(string key)
    {
        var entities = _dbContext.Database
            .Table<AppSettingEntity>()
            .Where(e => e.Key == key && e.DeviceId == _deviceId)
            .ToList();

        foreach (var entity in entities)
        {
            _dbContext.Database.Delete(entity);
        }

        _cache.TryRemove(key, out _);
        return true;
    }

    public bool FlushSettings() => true;
}
