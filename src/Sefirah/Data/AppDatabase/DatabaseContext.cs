using NotifyRelay.Data.AppDatabase.Models;
using NotifyRelay.Data.Models;
using NotifyRelay.Utils.Serialization;
using SQLite;
namespace NotifyRelay.Data.AppDatabase;

public class DatabaseContext : IDisposable
{
    public SQLiteConnection Database { get; private set; }
    private bool _disposed = false;

    public DatabaseContext(ILogger<DatabaseContext> logger)
    {
        try
        {
            logger.LogInformation("正在初始化数据库上下文");
            Database = TryCreateDatabase();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "初始化数据库上下文失败");
            throw;
        }
    }

    private static SQLiteConnection TryCreateDatabase()
    {
        var databasePath = Path.Combine(ApplicationData.Current.LocalFolder.Path, "NotifyRelay.db");
        var db = new SQLiteConnection(databasePath);

        if (db.GetTableInfo(nameof(LocalDeviceEntity)).Count == 0)
        {
            db.CreateTable<LocalDeviceEntity>();
        }
        else
        {
            var localColumns = db.GetTableInfo(nameof(LocalDeviceEntity));
            if (!localColumns.Any(col => col.Name.Equals("StateJson", StringComparison.OrdinalIgnoreCase)))
            {
                try
                {
                    db.Execute("ALTER TABLE LocalDeviceEntity ADD COLUMN StateJson TEXT DEFAULT ''");
                    // 从旧的 PrivateKey 列迁移数据
                    db.Execute("UPDATE LocalDeviceEntity SET StateJson = PrivateKey WHERE PrivateKey IS NOT NULL");
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Migration warning: Could not add StateJson column: {ex.Message}");
                }
            }
        }

        if (db.GetTableInfo(nameof(RemoteDeviceEntity)).Count == 0)
        {
            db.CreateTable<RemoteDeviceEntity>();
        }
        else
        {
            // Check if Model column exists, if not add it (migration for existing databases)
            var remoteDeviceColumns = db.GetTableInfo(nameof(RemoteDeviceEntity));
            var hasModelColumn = remoteDeviceColumns.Any(col => col.Name.Equals("Model", StringComparison.OrdinalIgnoreCase));
            var hasPublicKeyColumn = remoteDeviceColumns.Any(col => col.Name.Equals("PublicKey", StringComparison.OrdinalIgnoreCase));
            var hasSentftpRequestColumn = remoteDeviceColumns.Any(col => col.Name.Equals("HasSentftpRequest", StringComparison.OrdinalIgnoreCase));

            if (!hasModelColumn)
            {
                try
                {
                    db.Execute("ALTER TABLE RemoteDeviceEntity ADD COLUMN Model TEXT DEFAULT ''");
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Migration warning: Could not add Model column: {ex.Message}");
                }
            }

            if (!hasPublicKeyColumn)
            {
                try
                {
                    db.Execute("ALTER TABLE RemoteDeviceEntity ADD COLUMN PublicKey TEXT");
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Migration warning: Could not add PublicKey column: {ex.Message}");
                }
            }

            if (!hasSentftpRequestColumn)
            {
                try
                {
                    db.Execute("ALTER TABLE RemoteDeviceEntity ADD COLUMN HasSentftpRequest INTEGER DEFAULT 0");
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Migration warning: Could not add HasSentftpRequest column: {ex.Message}");
                }
            }
        }

        if (db.GetTableInfo(nameof(ApplicationInfoEntity)).Count == 0)
        {
            db.CreateTable<ApplicationInfoEntity>();
        }

        // 检查NotificationEntity表是否需要迁移（主键设计变更）
        Console.WriteLine("开始检查NotificationEntity表是否需要迁移...");
        var tableInfo = db.GetTableInfo(nameof(NotificationEntity));
        if (tableInfo.Count == 0)
        {
            Console.WriteLine("NotificationEntity表不存在，直接创建新表...");
            db.CreateTable<NotificationEntity>();
            Console.WriteLine("NotificationEntity新表创建成功");
        }
        else
        {
            Console.WriteLine("NotificationEntity表已存在，检查是否需要迁移...");
            bool needMigration = true;

            try
            {
                Console.WriteLine("尝试使用新的主键设计插入测试记录...");
                var testEntity = new NotificationEntity
                {
                    Id = "test|test|test|New",
                    DeviceIds = "[]",
                    DeviceNames = "[]",
                    MessageJson = "{}",
                    CreatedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
                };
                db.Insert(testEntity);
                db.Delete(testEntity);
                needMigration = false;
                Console.WriteLine("测试记录插入成功，不需要迁移");
            }
            catch (Exception)
            {
                Console.WriteLine("测试记录插入失败，需要进行表迁移");
            }

            if (needMigration)
            {
                try
                {
                    Console.WriteLine("开始迁移NotificationEntity表...");
                    var oldNotifications = db.Query<dynamic>("SELECT * FROM NotificationEntity");
                    Console.WriteLine($"查询到{oldNotifications.Count}条旧记录");

                    Console.WriteLine("删除旧表...");
                    db.Execute("DROP TABLE IF EXISTS NotificationEntity");
                    Console.WriteLine("旧表删除成功");

                    Console.WriteLine("创建新表...");
                    db.CreateTable<NotificationEntity>();
                    Console.WriteLine("新表创建成功");

                    Console.WriteLine("开始恢复数据到新表...");
                    int migratedCount = 0;
                    foreach (var oldRecord in oldNotifications)
                    {
                        try
                        {
                            var oldId = oldRecord.Id as string ?? string.Empty;
                            var notificationKey = oldRecord.NotificationKey as string ?? string.Empty;
                            var messageJson = oldRecord.MessageJson as string ?? string.Empty;
                            var pinned = (bool)(oldRecord.Pinned ?? false);
                            var createdAt = (long)(oldRecord.CreatedAt ?? DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());

                            var deviceId = oldId.Split('|')[0];

                            Console.WriteLine($"反序列化消息... record {migratedCount + 1}");
                            var message = SocketMessageSerializer.DeserializeMessage(messageJson) as NotificationMessage;

                            if (message != null)
                            {
                                var newId = $"{message.AppPackage}|{message.Title}|{message.Text}|{message.NotificationType}";

                                var newEntity = new NotificationEntity
                                {
                                    Id = newId,
                                    NotificationKey = notificationKey,
                                    DeviceIds = JsonSerializer.Serialize(new List<string> { deviceId }),
                                    DeviceNames = JsonSerializer.Serialize(new List<string> { deviceId }),
                                    MessageJson = messageJson,
                                    Pinned = pinned,
                                    CreatedAt = createdAt
                                };

                                db.Insert(newEntity);
                                migratedCount++;
                                Console.WriteLine($"记录 {migratedCount} 迁移成功");
                            }
                        }
                        catch (Exception innerEx)
                        {
                            Console.WriteLine($"Migration warning: Could not migrate notification record: {innerEx.Message}");
                        }
                    }

                    Console.WriteLine($"NotificationEntity表迁移成功，共迁移{migratedCount}条记录");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Migration error: Could not migrate NotificationEntity table: {ex.Message}");
                    db.Execute("DROP TABLE IF EXISTS NotificationEntity");
                    db.CreateTable<NotificationEntity>();
                }
            }
        }

        // 运行版本化迁移系统
        RunMigrations(db);

        return db;
    }

    /// <summary>
    /// 数据库版本迁移（使用 PRAGMA user_version）
    /// 当前版本: 2
    /// </summary>
    private static void RunMigrations(SQLiteConnection db)
    {
        // 向后兼容：从旧 SchemaVersionEntity 表读取版本，然后删除该表
        if (db.GetTableInfo(nameof(SchemaVersionEntity)).Count > 0)
        {
            var oldEntity = db.Find<SchemaVersionEntity>(1);
            if (oldEntity != null && oldEntity.Version > 0)
                db.Execute("PRAGMA user_version = " + oldEntity.Version);
            db.Execute("DROP TABLE IF EXISTS SchemaVersionEntity");
        }

        // 确保核心表存在（不依赖 version 检查，防止旧数据库缺表）
        if (db.GetTableInfo(nameof(FilterConfigEntity)).Count == 0)
            db.CreateTable<FilterConfigEntity>();

        if (db.GetTableInfo(nameof(AppSettingEntity)).Count == 0)
            db.CreateTable<AppSettingEntity>();

        int version = db.ExecuteScalar<int>("PRAGMA user_version");

        if (version == 0)
        {
            Console.WriteLine("开始迁移 v1: 过滤配置表...");

            var tableExists = db.GetTableInfo(nameof(FilterConfigEntity)).Count > 0;
            var hasOldJsonColumn = false;
            object? oldRowData = null;

            if (tableExists)
            {
                var columns = db.GetTableInfo(nameof(FilterConfigEntity));
                hasOldJsonColumn = columns.Any(c => c.Name.Equals("ConfigJson", StringComparison.OrdinalIgnoreCase));

                if (hasOldJsonColumn)
                {
                    Console.WriteLine("检测到旧版 FilterConfigEntity (ConfigJson 列)，读取数据...");
                    try
                    {
                        var rows = db.Query<dynamic>("SELECT * FROM FilterConfigEntity WHERE Id = 1");
                        if (rows.Count > 0)
                            oldRowData = (string)(rows[0].ConfigJson ?? "{}");
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"读取旧 ConfigJson 失败: {ex.Message}");
                    }
                }
            }

            var jsonFilePath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "NotifyRelay",
                "filter_config.json");

            Services.Filters.FilterConfig? importedConfig = null;

            if (oldRowData != null)
            {
                try
                {
                    importedConfig = JsonSerializer.Deserialize<Services.Filters.FilterConfig>((string)oldRowData);
                    Console.WriteLine("从旧数据库 ConfigJson 导入配置成功");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"从旧数据库 ConfigJson 反序列化失败: {ex.Message}");
                }
            }

            if (importedConfig == null && System.IO.File.Exists(jsonFilePath))
            {
                try
                {
                    var json = System.IO.File.ReadAllText(jsonFilePath);
                    importedConfig = JsonSerializer.Deserialize<Services.Filters.FilterConfig>(json);
                    Console.WriteLine("从旧 JSON 文件导入配置成功");
                    try { System.IO.File.Delete(jsonFilePath); } catch { }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"从旧 JSON 文件导入失败: {ex.Message}");
                }
            }

            if (hasOldJsonColumn)
            {
                db.Execute("DROP TABLE IF EXISTS FilterConfigEntity");
                Console.WriteLine("旧版 FilterConfigEntity 表已删除");
                db.CreateTable<FilterConfigEntity>();
                Console.WriteLine("FilterConfigEntity 表创建成功");
            }

            var entity = EntityFromConfig(importedConfig ?? new Services.Filters.FilterConfig());
            db.InsertOrReplace(entity);
            Console.WriteLine("过滤配置已写入数据库");

            db.Execute("PRAGMA user_version = 1");
            version = 1;
            Console.WriteLine("数据库已升级到 v1");
        }

        if (version == 1)
        {
            Console.WriteLine("开始迁移 v2: 设置配置表...");

            var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            var basePath = ApplicationData.Current.LocalFolder.Path;

            var oldSettingsPath = Path.Combine(basePath, "settings", "user_settings.json");
            if (File.Exists(oldSettingsPath))
            {
                try
                {
                    var json = File.ReadAllText(oldSettingsPath);
                    if (!string.IsNullOrWhiteSpace(json) && json != "null")
                    {
                        var dict = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json);
                        if (dict != null)
                        {
                            foreach (var kvp in dict)
                            {
                                var valueStr = kvp.Value.ValueKind == JsonValueKind.Null ? null : kvp.Value.GetRawText();
                                db.InsertOrReplace(new AppSettingEntity
                                {
                                    Key = kvp.Key,
                                    Value = valueStr,
                                    DeviceId = null,
                                    UpdatedAt = now
                                });
                            }
                            Console.WriteLine($"从 user_settings.json 导入 {dict.Count} 个设置项");
                        }
                    }
                    File.Delete(oldSettingsPath);
                    Console.WriteLine("已删除旧 user_settings.json");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"导入 user_settings.json 失败: {ex.Message}");
                }
            }

            var devicesDir = Path.Combine(basePath, "settings", "Devices");
            if (Directory.Exists(devicesDir))
            {
                foreach (var deviceFile in Directory.GetFiles(devicesDir, "device_*.json"))
                {
                    try
                    {
                        var fileName = Path.GetFileNameWithoutExtension(deviceFile);
                        var deviceId = fileName.StartsWith("device_") ? fileName[7..] : fileName;

                        var json = File.ReadAllText(deviceFile);
                        if (!string.IsNullOrWhiteSpace(json) && json != "null")
                        {
                            var dict = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json);
                            if (dict != null)
                            {
                                foreach (var kvp in dict)
                                {
                                    var valueStr = kvp.Value.ValueKind == JsonValueKind.Null ? null : kvp.Value.GetRawText();
                                    db.InsertOrReplace(new AppSettingEntity
                                    {
                                        Key = kvp.Key,
                                        Value = valueStr,
                                        DeviceId = deviceId,
                                        UpdatedAt = now
                                    });
                                }
                                Console.WriteLine($"从 {fileName}.json 导入 {dict.Count} 个设备设置项");
                            }
                        }
                        File.Delete(deviceFile);
                        Console.WriteLine($"已删除旧 {fileName}.json");
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"导入设备设置文件 {deviceFile} 失败: {ex.Message}");
                    }
                }

                try { Directory.Delete(devicesDir, false); } catch { }
            }

            try { Directory.Delete(Path.Combine(basePath, "settings"), false); } catch { }

            db.Execute("PRAGMA user_version = 2");
            Console.WriteLine("数据库已升级到 v2");
        }

        // 后续版本追加在此
        // if (version == 2) { ... db.Execute("PRAGMA user_version = 3"); }
    }

    internal static FilterConfigEntity EntityFromConfig(Services.Filters.FilterConfig config)
    {
        return new FilterConfigEntity
        {
            Id = 1,
            FilterSelf = config.FilterSelf,
            FilterNoTitleOrText = config.FilterNoTitleOrText,
            EnablePackageGroupMapping = config.EnablePackageGroupMapping,
            EnableDeduplication = config.EnableDeduplication,
            EnablePeerMode = config.EnablePeerMode,
            FilterMode = config.FilterMode,
            LocalFilterEntriesJson = JsonSerializer.Serialize(config.LocalFilterEntries),
            EnabledLocalFilterEntryIdsJson = JsonSerializer.Serialize(config.EnabledLocalFilterEntryIds.ToList()),
            PackageGroupsJson = JsonSerializer.Serialize(config.PackageGroups),
            PackageGroupEnabledJson = JsonSerializer.Serialize(config.PackageGroupEnabled),
            FilterListJson = JsonSerializer.Serialize(config.FilterList),
            UpdatedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
        };
    }

    public static Services.Filters.FilterConfig ConfigFromEntity(FilterConfigEntity entity)
    {
        var config = new Services.Filters.FilterConfig
        {
            FilterSelf = entity.FilterSelf,
            FilterNoTitleOrText = entity.FilterNoTitleOrText,
            EnablePackageGroupMapping = entity.EnablePackageGroupMapping,
            EnableDeduplication = entity.EnableDeduplication,
            EnablePeerMode = entity.EnablePeerMode,
            FilterMode = entity.FilterMode,
            LocalFilterEntries = DeserializeOrDefault<List<FilterEntry>>(entity.LocalFilterEntriesJson, []),
            EnabledLocalFilterEntryIds = DeserializeOrDefault<List<string>>(entity.EnabledLocalFilterEntryIdsJson, [])
                .ToHashSet(),
            PackageGroups = DeserializeOrDefault<List<List<string>>>(entity.PackageGroupsJson, []),
            PackageGroupEnabled = DeserializeOrDefault<List<bool>>(entity.PackageGroupEnabledJson, [true, true, true]),
            FilterList = DeserializeOrDefault<List<FilterListEntry>>(entity.FilterListJson, [])
        };

        // 确保默认值
        if (config.PackageGroups.Count == 0)
            config.PackageGroups = Services.Filters.FilterConfig.DefaultPackageGroups();
        while (config.PackageGroupEnabled.Count < config.PackageGroups.Count)
            config.PackageGroupEnabled.Add(true);

        return config;
    }

    private static T DeserializeOrDefault<T>(string json, T defaultValue) where T : class
    {
        try
        {
            if (string.IsNullOrWhiteSpace(json) || json == "{}")
                return defaultValue;
            return JsonSerializer.Deserialize<T>(json) ?? defaultValue;
        }
        catch
        {
            return defaultValue;
        }
    }

    // 实现 IDisposable 接口
    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (!_disposed)
        {
            if (disposing)
            {
                Database?.Dispose();
                Database = null!;
            }
            _disposed = true;
        }
    }

    ~DatabaseContext()
    {
        Dispose(false);
    }
}
