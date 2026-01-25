using System.Diagnostics;
using System.Text.Json;
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
            logger.LogError(ex, "初始化数据库上下文失败：{ex}");
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
            var hasSentSftpRequestColumn = remoteDeviceColumns.Any(col => col.Name.Equals("HasSentSftpRequest", StringComparison.OrdinalIgnoreCase));
            
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
            
            if (!hasSentSftpRequestColumn)
            {
                try
                {
                    db.Execute("ALTER TABLE RemoteDeviceEntity ADD COLUMN HasSentSftpRequest INTEGER DEFAULT 0");
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Migration warning: Could not add HasSentSftpRequest column: {ex.Message}");
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
            // 新表，直接创建
            db.CreateTable<NotificationEntity>();
            Console.WriteLine("NotificationEntity新表创建成功");
        }
        else
        {
            Console.WriteLine("NotificationEntity表已存在，检查是否需要迁移...");
            // 检查是否需要迁移（旧表使用deviceId|notificationKey作为主键，新表使用内容哈希作为主键）
            bool needMigration = true;
            
            try
            {
                Console.WriteLine("尝试使用新的主键设计插入测试记录...");
                // 尝试使用新的主键设计插入一条测试记录
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
                // 插入失败，需要迁移
            }
            
            if (needMigration)
            {
                try
                {
                    Console.WriteLine("开始迁移NotificationEntity表...");
                    // 1. 使用动态类型查询旧表数据，避免字段不匹配问题
                    Console.WriteLine("查询旧表数据...");
                    var oldNotifications = db.Query<dynamic>("SELECT * FROM NotificationEntity");
                    Console.WriteLine($"查询到{oldNotifications.Count}条旧记录");
                    
                    // 2. 删除旧表
                    Console.WriteLine("删除旧表...");
                    db.Execute("DROP TABLE IF EXISTS NotificationEntity");
                    Console.WriteLine("旧表删除成功");
                    
                    // 3. 创建新表（使用新的主键设计）
                    Console.WriteLine("创建新表...");
                    db.CreateTable<NotificationEntity>();
                    Console.WriteLine("新表创建成功");
                    
                    // 4. 恢复数据到新表
                    Console.WriteLine("开始恢复数据到新表...");
                    int migratedCount = 0;
                    foreach (var oldRecord in oldNotifications)
                    {
                        try
                        {
                            // 获取旧记录的字段值
                            var oldId = oldRecord.Id as string ?? string.Empty;
                            var notificationKey = oldRecord.NotificationKey as string ?? string.Empty;
                            var messageJson = oldRecord.MessageJson as string ?? string.Empty;
                            var pinned = (bool)(oldRecord.Pinned ?? false);
                            var createdAt = (long)(oldRecord.CreatedAt ?? DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
                            
                            // 从旧Id中提取设备ID (旧Id格式：deviceId|notificationKey)
                            var deviceId = oldId.Split('|')[0];
                            
                            // 反序列化消息
                            Console.WriteLine($"反序列化消息... record {migratedCount + 1}");
                            var message = SocketMessageSerializer.DeserializeMessage(messageJson) as NotificationMessage;
                            
                            if (message != null)
                            {
                                // 生成新的主键
                                var newId = $"{message.AppPackage}|{message.Title}|{message.Text}|{message.NotificationType}";
                                
                                // 创建新实体，为DeviceIds和DeviceNames设置正确的JSON格式
                                // 使用设备ID作为名称占位符，后续在EnsureNotificationsLoadedAsync中会更新为正确的设备名称
                                var newEntity = new NotificationEntity
                                {
                                    Id = newId,
                                    NotificationKey = notificationKey,
                                    DeviceIds = JsonSerializer.Serialize(new List<string> { deviceId }),
                                    DeviceNames = JsonSerializer.Serialize(new List<string> { deviceId }), // 临时使用设备ID作为名称
                                    MessageJson = messageJson,
                                    Pinned = pinned,
                                    CreatedAt = createdAt
                                };
                                
                                // 保存到新表
                                db.Insert(newEntity);
                                migratedCount++;
                                Console.WriteLine($"记录 {migratedCount} 迁移成功");
                            }
                        }
                        catch (Exception innerEx)
                        {
                            Console.WriteLine($"Migration warning: Could not migrate notification record: {innerEx.Message}");
                            // 跳过有问题的记录，继续迁移其他记录
                        }
                    }
                    
                    Console.WriteLine($"NotificationEntity表迁移成功，共迁移{migratedCount}条记录");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Migration error: Could not migrate NotificationEntity table: {ex.Message}");
                    // 如果迁移失败，至少创建一个空表
                    db.Execute("DROP TABLE IF EXISTS NotificationEntity");
                    db.CreateTable<NotificationEntity>();
                }
            }
        }

        return db;
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
                // 释放托管资源
                Database?.Dispose();
                Database = null;
            }
            _disposed = true;
        }
    }

    // 析构函数
    ~DatabaseContext()
    {
        Dispose(false);
    }
}
