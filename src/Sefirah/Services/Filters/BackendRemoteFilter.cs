using NotifyRelay.Data.Enums;
using NotifyRelay.Data.Models;
using NotifyRelay.Native;

namespace NotifyRelay.Services.Filters;

/// <summary>
/// 远程通知过滤器（Android → PC 方向）
/// 决定 Android 端发来的通知是否在 PC 上显示
/// 功能对齐 Android 端 BackendRemoteFilter：
/// - 包名等价组映射（合一化）— 委托给 Rust Core
/// - 过滤模式：none / black / white / peer — 委托给 Rust Core
/// - 文本去重（80% 匹配率）— 委托给 Rust Core
/// </summary>
public class BackendRemoteFilter
{
    private readonly ILogger<BackendRemoteFilter> _logger;

    // 最近通知的去重缓存 (title, text, timestamp)
    private readonly LinkedList<(string Title, string Text, DateTime Timestamp)> _recentNotifications = new();
    private const int MaxRecentNotifications = 200;
    private static readonly TimeSpan DedupWindow = TimeSpan.FromSeconds(30);

    public BackendRemoteFilter(ILogger<BackendRemoteFilter> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// 过滤结果
    /// </summary>
    public class FilterResult
    {
        public bool ShouldShow { get; private set; }
        public string MappedPackageName { get; private set; }
        public string? DisplayAppName { get; private set; }

        private FilterResult(bool shouldShow, string mappedPkg, string? appName)
        {
            ShouldShow = shouldShow;
            MappedPackageName = mappedPkg;
            DisplayAppName = appName;
        }

        public static FilterResult Blocked => new(false, string.Empty, null);
        public static FilterResult Passed(string mappedPkg, string? appName = null) =>
            new(true, mappedPkg, appName);
    }

    // ====== 运行时配置（由 FilterConfig 加载后设置） ======

    /// <summary>包名等价组映射开关</summary>
    public bool EnablePackageGroupMapping { get; set; } = true;

    /// <summary>包名等价组</summary>
    public List<List<string>> PackageGroups { get; set; } = [];

    /// <summary>各组启用状态</summary>
    public List<bool> PackageGroupEnabled { get; set; } = [];

    /// <summary>智能去重开关</summary>
    public bool EnableDeduplication { get; set; } = true;

    /// <summary>过滤模式: none / black / white / peer</summary>
    public string FilterMode { get; set; } = "none";

    /// <summary>黑白名单列表</summary>
    public List<FilterListEntry> FilterList { get; set; } = [];

    /// <summary>对等模式开关</summary>
    public bool EnablePeerMode { get; set; } = false;

    /// <summary>本机已安装的应用包名列表（用于 peer 模式判断）</summary>
    public HashSet<string> InstalledPackageNames { get; set; } = [];

    // ====== 核心方法 ======

    /// <summary>
    /// 便捷方法：是否应阻止该通知
    /// </summary>
    public bool ShouldBlock(NotificationType notificationType, string? title, string? appPackage, string? appName, string? text)
    {
        var pkg = appPackage ?? string.Empty;

        var mappedPkg = MapToLocalPackage(pkg);
        if (EnablePeerMode && !string.IsNullOrEmpty(mappedPkg) &&
            InstalledPackageNames.Contains(mappedPkg))
        {
            return true;
        }

        return false;
    }

    /// <summary>
    /// 包名等价组映射 — 委托给 Rust Core
    /// </summary>
    public string MapToLocalPackage(string pkg)
    {
        var result = NotifyRelayCore.Safe.MapLocalPackage(NativeCore.Context, pkg);
        return result ?? pkg;
    }

    /// <summary>
    /// 检查过滤模式 — 委托给 Rust Core
    /// </summary>
    private bool CheckFilterMode(string mappedPkg, string originalPkg, string title, string text)
    {
        var result = NotifyRelayCore.Safe.CheckFilterMode(NativeCore.Context, mappedPkg, originalPkg, title, text);
        return result != 0;
    }

    // ====== 文本去重 ======

    /// <summary>
    /// 判断是否与最近通知重复（80%+ 匹配率）— 委托给 Rust Core
    /// </summary>
    private bool IsDuplicate(string newTitle, string newText)
    {
        CleanupStaleEntries();

        lock (_recentNotifications)
        {
            foreach (var (existingTitle, existingText, _) in _recentNotifications)
            {
                if (NotifyRelayCore.Safe.ShouldDeduplicate(newTitle, newText, existingTitle, existingText) != 0)
                    return true;
            }
        }

        return false;
    }

    /// <summary>
    /// 记录通知到去重缓存
    /// </summary>
    public void RecordNotification(string title, string text)
    {
        lock (_recentNotifications)
        {
            _recentNotifications.AddFirst((title, text, DateTime.UtcNow));

            while (_recentNotifications.Count > MaxRecentNotifications)
                _recentNotifications.RemoveLast();
        }
    }

    private void CleanupStaleEntries()
    {
        var cutoff = DateTime.UtcNow - DedupWindow;
        lock (_recentNotifications)
        {
            while (_recentNotifications.Last?.Value.Timestamp < cutoff)
                _recentNotifications.RemoveLast();
        }
    }
}
