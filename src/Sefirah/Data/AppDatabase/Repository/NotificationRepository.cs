using NotifyRelay.Data.AppDatabase.Models;
using NotifyRelay.Data.Models;
using NotifyRelay.Utils.Serialization;

namespace NotifyRelay.Data.AppDatabase.Repository;

public class NotificationRepository(DatabaseContext context, ILogger logger)
{
    public List<NotificationEntity> GetAllNotifications(int take = 500)
    {
        try
        {
            return context.Database.Table<NotificationEntity>()
                .OrderByDescending(n => n.CreatedAt)
                .Take(take)
                .ToList();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "获取所有通知失败");
            return [];
        }
    }

    public List<NotificationEntity> GetDeviceNotifications(string deviceId, int take = 200)
    {
        try
        {
            // 获取所有通知，然后筛选包含该设备ID的通知
            var allNotifications = context.Database.Table<NotificationEntity>()
                .OrderByDescending(n => n.CreatedAt)
                .Take(take)
                .ToList();

            return allNotifications.Where(n =>
            {
                try
                {
                    var deviceIds = JsonSerializer.Deserialize<List<string>>(n.DeviceIds);
                    return deviceIds?.Contains(deviceId) ?? false;
                }
                catch
                {
                    return false;
                }
            }).ToList();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "加载设备 {DeviceId} 的通知失败", deviceId);
            return [];
        }
    }

    public void UpsertNotification(string deviceId, string payload, bool pinned)
    {
        string notificationKey = Guid.NewGuid().ToString();
        try
        {
            using var doc = JsonDocument.Parse(payload);
            var root = doc.RootElement;

            var appPackage = (root.TryGetProperty("packageName", out var pn) && pn.ValueKind == JsonValueKind.String ? pn.GetString() : null)
                ?? (root.TryGetProperty("appPackage", out var ap) && ap.ValueKind == JsonValueKind.String ? ap.GetString() : null);
            var title = root.TryGetProperty("title", out var tl) ? tl.GetString() : null;
            var text = root.TryGetProperty("text", out var tx) ? tx.GetString() : null;
            var notificationType = root.TryGetProperty("notificationType", out var nt) && nt.ValueKind == JsonValueKind.String ? nt.GetString() : "New";
            if (root.TryGetProperty("notificationKey", out var nk) && nk.ValueKind == JsonValueKind.String)
                notificationKey = nk.GetString()!;
            var timeStamp = root.TryGetProperty("timeStamp", out var ts) && ts.ValueKind == JsonValueKind.String ? ts.GetString() : DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString();

            // 生成聚合键作为主键
            var aggregationKey = $"{appPackage}|{title}|{text}|{notificationType}";
            var entity = context.Database.Find<NotificationEntity>(aggregationKey);

            List<string> deviceIds = [];
            List<string> deviceNames = [];

            if (entity is not null)
            {
                try
                {
                    deviceIds = JsonSerializer.Deserialize<List<string>>(entity.DeviceIds) ?? [];
                    deviceNames = JsonSerializer.Deserialize<List<string>>(entity.DeviceNames) ?? [];
                }
                catch
                {
                    deviceIds = [];
                    deviceNames = [];
                }
            }

            if (!deviceIds.Contains(deviceId))
            {
                deviceIds.Add(deviceId);
                deviceNames.Add(deviceId);
            }

            var updatedEntity = new NotificationEntity
            {
                Id = aggregationKey,
                NotificationKey = notificationKey,
                DeviceIds = JsonSerializer.Serialize(deviceIds),
                DeviceNames = JsonSerializer.Serialize(deviceNames),
                MessageJson = payload,
                Pinned = pinned,
                CreatedAt = ParseTimestamp(timeStamp)
            };

            context.Database.InsertOrReplace(updatedEntity);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "保存/更新设备 {DeviceId} 的通知 {Key} 失败", deviceId, notificationKey);
        }
    }

    public void DeleteNotification(string deviceId, string notificationKey)
    {
        try
        {
            // 查找所有包含该通知键的通知
            var allNotifications = context.Database.Table<NotificationEntity>().ToList();

            foreach (var entity in allNotifications)
            {
                try
                {
                    var deviceIds = JsonSerializer.Deserialize<List<string>>(entity.DeviceIds);
                    if (deviceIds?.Contains(deviceId) ?? false)
                    {
                        // 如果是最后一个设备，删除整个通知
                        if (deviceIds.Count == 1)
                        {
                            context.Database.Delete(entity);
                        }
                        else
                        {
                            // 否则，从设备列表中移除该设备
                            var deviceNames = JsonSerializer.Deserialize<List<string>>(entity.DeviceNames) ?? [];
                            var index = deviceIds.IndexOf(deviceId);

                            if (index >= 0)
                            {
                                deviceIds.RemoveAt(index);
                                if (index < deviceNames.Count)
                                {
                                    deviceNames.RemoveAt(index);
                                }

                                entity.DeviceIds = JsonSerializer.Serialize(deviceIds);
                                entity.DeviceNames = JsonSerializer.Serialize(deviceNames);
                                context.Database.InsertOrReplace(entity);
                            }
                        }
                    }
                }
                catch (Exception innerEx)
                {
                    logger.LogError(innerEx, "处理通知 {Id} 时出错", entity.Id);
                }
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "删除设备 {DeviceId} 的通知 {Key} 失败", deviceId, notificationKey);
        }
    }

    public void ClearDeviceNotifications(string deviceId)
    {
        try
        {
            var allNotifications = context.Database.Table<NotificationEntity>().ToList();

            foreach (var entity in allNotifications)
            {
                try
                {
                    var deviceIds = JsonSerializer.Deserialize<List<string>>(entity.DeviceIds);
                    if (deviceIds?.Contains(deviceId) ?? false)
                    {
                        // 如果是最后一个设备，删除整个通知
                        if (deviceIds.Count == 1)
                        {
                            context.Database.Delete(entity);
                        }
                        else
                        {
                            // 否则，从设备列表中移除该设备
                            var deviceNames = JsonSerializer.Deserialize<List<string>>(entity.DeviceNames) ?? [];
                            var index = deviceIds.IndexOf(deviceId);

                            if (index >= 0)
                            {
                                deviceIds.RemoveAt(index);
                                if (index < deviceNames.Count)
                                {
                                    deviceNames.RemoveAt(index);
                                }

                                entity.DeviceIds = JsonSerializer.Serialize(deviceIds);
                                entity.DeviceNames = JsonSerializer.Serialize(deviceNames);
                                context.Database.InsertOrReplace(entity);
                            }
                        }
                    }
                }
                catch (Exception innerEx)
                {
                    logger.LogError(innerEx, "处理通知 {Id} 时出错", entity.Id);
                }
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "清空设备 {DeviceId} 的通知失败", deviceId);
        }
    }

    public void ClearDeviceNotificationsExceptPinned(string deviceId)
    {
        try
        {
            var allNotifications = context.Database.Table<NotificationEntity>().Where(n => !n.Pinned).ToList();

            foreach (var entity in allNotifications)
            {
                try
                {
                    var deviceIds = JsonSerializer.Deserialize<List<string>>(entity.DeviceIds);
                    if (deviceIds?.Contains(deviceId) ?? false)
                    {
                        // 如果是最后一个设备，删除整个通知
                        if (deviceIds.Count == 1)
                        {
                            context.Database.Delete(entity);
                        }
                        else
                        {
                            // 否则，从设备列表中移除该设备
                            var deviceNames = JsonSerializer.Deserialize<List<string>>(entity.DeviceNames) ?? [];
                            var index = deviceIds.IndexOf(deviceId);

                            if (index >= 0)
                            {
                                deviceIds.RemoveAt(index);
                                if (index < deviceNames.Count)
                                {
                                    deviceNames.RemoveAt(index);
                                }

                                entity.DeviceIds = JsonSerializer.Serialize(deviceIds);
                                entity.DeviceNames = JsonSerializer.Serialize(deviceNames);
                                context.Database.InsertOrReplace(entity);
                            }
                        }
                    }
                }
                catch (Exception innerEx)
                {
                    logger.LogError(innerEx, "处理通知 {Id} 时出错", entity.Id);
                }
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "清空设备 {DeviceId} 的未置顶通知失败", deviceId);
        }
    }

    public void UpdatePinned(string deviceId, string notificationKey, bool pinned)
    {
        try
        {
            // 查找所有包含该通知键的通知
            var allNotifications = context.Database.Table<NotificationEntity>().ToList();

            foreach (var entity in allNotifications)
            {
                try
                {
                    var deviceIds = JsonSerializer.Deserialize<List<string>>(entity.DeviceIds);
                    if (deviceIds?.Contains(deviceId) ?? false)
                    {
                        entity.Pinned = pinned;
                        context.Database.InsertOrReplace(entity);
                    }
                }
                catch (Exception innerEx)
                {
                    logger.LogError(innerEx, "处理通知 {Id} 时出错", entity.Id);
                }
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "更新设备 {DeviceId} 的通知 {Key} 的置顶状态失败", deviceId, notificationKey);
        }
    }

    private static long ParseTimestamp(string? timestamp)
    {
        if (long.TryParse(timestamp, out var ts)) return ts;
        return DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
    }
}
