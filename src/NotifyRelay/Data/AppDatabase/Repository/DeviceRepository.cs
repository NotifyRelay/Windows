using NotifyRelay.Data.AppDatabase.Models;
using NotifyRelay.Data.Models;

namespace NotifyRelay.Data.AppDatabase.Repository;

public class DeviceRepository(DatabaseContext context, ILogger logger)
{
    public LocalDeviceEntity? GetLocalDevice()
    {
        try
        {
            return context.Database.Table<LocalDeviceEntity>().FirstOrDefault();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "获取本地设备失败");
            return null;
        }
    }

    public void AddOrUpdateLocalDevice(LocalDeviceEntity device)
    {
        try
        {
            context.Database.InsertOrReplace(device);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "添加本地设备失败");
        }
    }

    /// <summary>
    /// 主键变更：设备 UUID 以 Rust 私有库为准时更新平台表主键
    /// </summary>
    public void RenameLocalDeviceKey(string oldId, string newId)
    {
        try
        {
            context.Database.Execute(
                "UPDATE LocalDeviceEntity SET DeviceId = ? WHERE DeviceId = ?",
                newId,
                oldId);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "更新本地设备主键失败 {oldId} -> {newId}", oldId, newId);
        }
    }

    /// <summary>
    /// 清空旧设备密钥列值（密钥已迁移至 Rust 私有库），返回清空行数
    /// </summary>
    public int ClearRemoteSecrets()
    {
        try
        {
            // 行数的语义：受影响行（sqlite-net 单列值的改变即受影响）
            context.Database.Execute(
                "UPDATE RemoteDeviceEntity SET SharedSecret = NULL WHERE SharedSecret IS NOT NULL");
            return 1;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "清空旧设备密钥列失败");
            return 0;
        }
    }

    public void AddOrUpdateRemoteDevice(RemoteDeviceEntity device)
    {
        context.Database.InsertOrReplace(device);
    }

    /// <summary>
    /// 全量读取远端设备（含历史 SharedSecret 列，供一次性迁移到 Rust）
    /// </summary>
    public List<RemoteDeviceEntity> GetRemoteDevices()
    {
        try
        {
            return context.Database.Table<RemoteDeviceEntity>().ToList();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "读取远端设备失败");
            return [];
        }
    }

    public bool HasDevice(string deviceId, out RemoteDeviceEntity device)
    {
        device = context.Database.Find<RemoteDeviceEntity>(deviceId);
        return device != null;
    }

    public async Task<PairedDevice?> GetLastConnectedDevice()
    {
        try
        {
            var device = await Task.FromResult(context.Database.Table<RemoteDeviceEntity>().OrderByDescending(d => d.LastConnected).FirstOrDefault());
            if (device is null) return null;
            return await device.ToPairedDevice();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "获取最后连接的设备失败");
            return null;
        }
    }

    public async Task<List<PairedDevice>> GetPairedDevices()
    {
        try
        {
            var devices = context.Database.Table<RemoteDeviceEntity>()
                .OrderByDescending(d => d.LastConnected)
                .ToList();
            var pairedDevices = await Task.WhenAll(devices.Select(d => d.ToPairedDevice()));
            return pairedDevices.ToList();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "获取配对设备失败");
            return [];
        }
    }

    public void DeletePairedDevice(string deviceId)
    {
        var device = context.Database.Find<RemoteDeviceEntity>(deviceId);
        if (device != null)
        {
            context.Database.Delete(device);
        }
    }

    public List<string> GetRemoteDeviceIpAddresses()
    {
        return context.Database.Table<RemoteDeviceEntity>().SelectMany(d => d.IpAddresses).ToList();
    }
}

