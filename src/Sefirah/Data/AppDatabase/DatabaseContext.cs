using System.Diagnostics;
using System.Text.Json;
using Sefirah.Data.AppDatabase.Models;
using SQLite;
namespace Sefirah.Data.AppDatabase;

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
        var databasePath = Path.Combine(ApplicationData.Current.LocalFolder.Path, "sefirah.db");
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

        if (db.GetTableInfo(nameof(NotificationEntity)).Count == 0)
        {
            db.CreateTable<NotificationEntity>();
        }
        else
        {
            // 检查NotificationEntity表的列是否需要迁移
            var notificationColumns = db.GetTableInfo(nameof(NotificationEntity));
            var hasDeviceIdColumn = notificationColumns.Any(col => col.Name.Equals("DeviceId", StringComparison.OrdinalIgnoreCase));
            var hasDeviceIdsColumn = notificationColumns.Any(col => col.Name.Equals("DeviceIds", StringComparison.OrdinalIgnoreCase));
            var hasDeviceNamesColumn = notificationColumns.Any(col => col.Name.Equals("DeviceNames", StringComparison.OrdinalIgnoreCase));
            
            // 如果存在旧的DeviceId列，需要进行迁移
            if (hasDeviceIdColumn)
            {
                // 添加新的DeviceIds和DeviceNames列
                if (!hasDeviceIdsColumn)
                {
                    try
                    {
                        db.Execute("ALTER TABLE NotificationEntity ADD COLUMN DeviceIds TEXT DEFAULT '[]'");
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"Migration warning: Could not add DeviceIds column: {ex.Message}");
                    }
                }
                
                if (!hasDeviceNamesColumn)
                {
                    try
                    {
                        db.Execute("ALTER TABLE NotificationEntity ADD COLUMN DeviceNames TEXT DEFAULT '[]'");
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"Migration warning: Could not add DeviceNames column: {ex.Message}");
                    }
                }
                
                // 将现有数据迁移到新列
                if (hasDeviceIdsColumn && hasDeviceNamesColumn)
                {
                    try
                    {
                        // 使用字符串拼接创建JSON数组，避免依赖SQLite的json_array函数
                        db.Execute("UPDATE NotificationEntity SET DeviceIds = '[' || quote(DeviceId) || ']', DeviceNames = '[' || quote(DeviceId) || ']'");
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"Migration warning: Could not migrate DeviceId to DeviceIds/DeviceNames: {ex.Message}");
                    }
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
