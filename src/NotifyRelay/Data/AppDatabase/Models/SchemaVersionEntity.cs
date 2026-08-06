using SQLite;

namespace NotifyRelay.Data.AppDatabase.Models;

public class SchemaVersionEntity
{
    [PrimaryKey]
    public int Id { get; set; } = 1;

    public int Version { get; set; }
}
