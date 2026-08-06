using SQLite;

namespace NotifyRelay.Data.AppDatabase.Models;

public class FilterConfigEntity
{
    [PrimaryKey]
    public int Id { get; set; } = 1;

    // ====== 标量字段 ======
    public bool FilterSelf { get; set; } = true;
    public bool FilterNoTitleOrText { get; set; } = true;
    public bool EnablePackageGroupMapping { get; set; } = true;
    public bool EnableDeduplication { get; set; } = true;
    public bool EnablePeerMode { get; set; } = false;
    public string FilterMode { get; set; } = "none";

    // ====== JSON 序列化字段（复杂类型） ======
    public string LocalFilterEntriesJson { get; set; } = "[]";
    public string EnabledLocalFilterEntryIdsJson { get; set; } = "[]";
    public string PackageGroupsJson { get; set; } = "[]";
    public string PackageGroupEnabledJson { get; set; } = "[true,true,true]";
    public string FilterListJson { get; set; } = "[]";

    public long UpdatedAt { get; set; }
}
