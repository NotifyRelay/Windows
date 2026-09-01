using SQLite;

namespace NotifyRelay.Data.AppDatabase.Models;

public class NotificationEntity
{
    [PrimaryKey]
    public string Id { get; set; } = string.Empty; // AppPackage|Title|Text|Type (聚合键)

    public string NotificationKey { get; set; } = string.Empty;
    public string DeviceIds { get; set; } = string.Empty; // JSON格式存储的设备ID列表，如：["device1","device2"]
    public string DeviceNames { get; set; } = string.Empty; // JSON格式存储的设备名称列表，如：["Device1","Device2"]

    // Raw serialized NotificationMessage payload for replay
    public string MessageJson { get; set; } = string.Empty;

    // Local-only flags
    public bool Pinned { get; set; } = false;

    // For ordering when timestamp is missing/duplicate
    public long CreatedAt { get; set; }
}
