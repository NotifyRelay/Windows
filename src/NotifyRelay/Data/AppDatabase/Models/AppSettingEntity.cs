using SQLite;

namespace NotifyRelay.Data.AppDatabase.Models;

[Table("AppSettingEntity")]
public class AppSettingEntity
{
    [PrimaryKey, AutoIncrement]
    public int Id { get; set; }

    [Indexed]
    public string Key { get; set; } = string.Empty;

    public string? Value { get; set; }

    [Indexed]
    public string? DeviceId { get; set; }

    public long UpdatedAt { get; set; }
}
