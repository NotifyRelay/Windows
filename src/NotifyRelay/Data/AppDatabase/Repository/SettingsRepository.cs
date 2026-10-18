using NotifyRelay.Data.AppDatabase.Models;

namespace NotifyRelay.Data.AppDatabase.Repository;

/// <summary>
/// 应用设置仓储（强类型）。
///
/// 直接读写 <see cref="AppSettingEntity"/> 表，取代原先基于 <c>IConfiguration</c> 的间接层
/// （<c>SqliteConfigurationSource</c> + <c>SettingsStorageHelper</c>）——那层抽象是为当年
/// 跨平台对齐 Preferences 风格 API 而引入的，本项目已是单一 WinUI 3 目标，不再需要。
///
/// 落盘格式与旧实现完全一致：<c>Key</c> = 设置名，<c>DeviceId</c> = null 表示全局设置，
/// <c>Value</c> = JSON 文本。因此升级用户无需任何数据迁移。
/// </summary>
public sealed class SettingsRepository
{
    /// <summary>设备 ID 与设置名在内存缓存键中的分隔符（避免与设置名中的字符冲突）。</summary>
    private const char CacheKeySeparator = '\u0001';

    private readonly DatabaseContext _dbContext;
    private readonly Lock _gate = new();

    /// <summary>内存缓存：键为 <c>deviceId + CacheKeySeparator + settingName</c>，值为原始 JSON 文本。</summary>
    private readonly Dictionary<string, string?> _cache = new(StringComparer.Ordinal);

    public SettingsRepository(DatabaseContext dbContext)
    {
        _dbContext = dbContext;
        Load();
    }

    private static string BuildCacheKey(string? deviceId, string settingName) =>
        deviceId is null ? settingName : deviceId + CacheKeySeparator + settingName;

    /// <summary>启动时一次性载入全部设置，避免每次读取属性都查询 SQLite。</summary>
    private void Load()
    {
        foreach (var entity in _dbContext.Database.Table<AppSettingEntity>().ToList())
        {
            _cache[BuildCacheKey(entity.DeviceId, entity.Key)] = entity.Value;
        }
    }

    /// <summary>读取全局设置。</summary>
    public T Get<T>(string settingName, T defaultValue) => Get(null, settingName, defaultValue);

    /// <summary>读取指定设备或全局（<paramref name="deviceId"/> 为 null）的设置。</summary>
    public T Get<T>(string? deviceId, string settingName, T defaultValue)
    {
        string? raw;
        lock (_gate)
        {
            if (!_cache.TryGetValue(BuildCacheKey(deviceId, settingName), out raw))
                return defaultValue;
        }

        if (raw is null)
            return defaultValue;

        try
        {
            return JsonSerializer.Deserialize<T>(raw) ?? defaultValue;
        }
        catch
        {
            // 与旧实现一致：解析失败时静默回退到默认值
            return defaultValue;
        }
    }

    /// <summary>写入全局设置，返回是否实际发生变化。</summary>
    public bool Set<T>(string settingName, T value) => Set(null, settingName, value);

    /// <summary>写入指定设备或全局（<paramref name="deviceId"/> 为 null）的设置，返回是否实际发生变化。</summary>
    public bool Set<T>(string? deviceId, string settingName, T value)
    {
        var newJson = value is null ? null : JsonSerializer.Serialize(value);
        var cacheKey = BuildCacheKey(deviceId, settingName);

        lock (_gate)
        {
            if (_cache.TryGetValue(cacheKey, out var existing)
                && string.Equals(existing, newJson, StringComparison.Ordinal))
            {
                return false;
            }

            _cache[cacheKey] = newJson;
            Persist(deviceId, settingName, newJson);
            return true;
        }
    }

    private void Persist(string? deviceId, string settingName, string? value)
    {
        var db = _dbContext.Database;
        var existing = db.Table<AppSettingEntity>()
            .Where(e => e.Key == settingName && e.DeviceId == deviceId)
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
                Key = settingName,
                Value = value,
                DeviceId = deviceId,
                UpdatedAt = now
            });
        }
    }
}
