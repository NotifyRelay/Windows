using NotifyRelay.Data.Models;

namespace NotifyRelay.Services.Filters;

/// <summary>
/// 过滤配置模型
/// 持久化由 FilterConfigRepository 负责（数据库存储）
/// </summary>
public class FilterConfig
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    // ====== 本地过滤配置 ======

    public bool FilterSelf { get; set; } = true;
    public bool FilterNoTitleOrText { get; set; } = true;
    public List<FilterEntry> LocalFilterEntries { get; set; } = [];
    public HashSet<string> EnabledLocalFilterEntryIds { get; set; } = [];

    // ====== 远程过滤配置 ======

    public bool EnablePackageGroupMapping { get; set; } = true;
    public List<List<string>> PackageGroups { get; set; } = DefaultPackageGroups();
    public List<bool> PackageGroupEnabled { get; set; } = [true, true, true];
    public bool EnableDeduplication { get; set; } = true;
    public string FilterMode { get; set; } = "none";
    public bool EnablePeerMode { get; set; } = false;
    public List<FilterListEntry> FilterList { get; set; } = [];

    public static List<List<string>> DefaultPackageGroups()
    {
        return
        [
            ["tv.danmaku.bilibilihd", "tv.danmaku.bili"],
            ["com.sina.weibo", "com.sina.weibog3", "com.weico.international"],
            ["com.tencent.mobileqq", "com.tencent.tim"]
        ];
    }

    /// <summary>
    /// 将配置应用到运行时过滤器
    /// </summary>
    public void ApplyTo(BackendRemoteFilter remoteFilter)
    {
        remoteFilter.EnablePackageGroupMapping = EnablePackageGroupMapping;
        remoteFilter.PackageGroups = PackageGroups;
        remoteFilter.PackageGroupEnabled = PackageGroupEnabled;
        remoteFilter.EnableDeduplication = EnableDeduplication;
        remoteFilter.FilterMode = FilterMode;
        remoteFilter.EnablePeerMode = EnablePeerMode;
        remoteFilter.FilterList = FilterList;
    }

    /// <summary>
    /// 刷新本地过滤缓存
    /// </summary>
    public void ApplyLocalFilter()
    {
        var enabledIds = EnabledLocalFilterEntryIds.Count > 0
            ? EnabledLocalFilterEntryIds
            : new HashSet<string>(LocalFilterEntries.Select(e => e.Id));

        BackendLocalFilter.FilterSelf = FilterSelf;
        BackendLocalFilter.FilterNoTitleOrText = FilterNoTitleOrText;
        BackendLocalFilter.RefreshCache(LocalFilterEntries, enabledIds);
    }
}
