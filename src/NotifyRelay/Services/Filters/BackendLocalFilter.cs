using NotifyRelay.Data.Models;

namespace NotifyRelay.Services.Filters;

/// <summary>
/// 本机通知过滤器（PC → Android 方向）
/// 决定哪些本机 Windows 通知应当转发到远端设备
/// 与 Android 端 BackendLocalFilter 逻辑对齐
/// </summary>
public static class BackendLocalFilter
{
    private const string SelfPackageIndicator = "notifyrelay";

    /// <summary>
    /// 过滤本应用自身通知
    /// </summary>
    public static bool FilterSelf { get; set; } = true;

    /// <summary>
    /// 过滤无标题且无内容的通知
    /// </summary>
    public static bool FilterNoTitleOrText { get; set; } = true;

    private static List<FilterEntry>? _cachedEntries;
    private static HashSet<string>? _cachedEnabledIds;

    /// <summary>
    /// 刷新过滤条目缓存
    /// </summary>
    public static void RefreshCache(List<FilterEntry> allEntries, HashSet<string> enabledIds)
    {
        _cachedEntries = allEntries;
        _cachedEnabledIds = enabledIds;
    }

    /// <summary>
    /// 判断本机通知是否应转发到远端
    /// </summary>
    public static bool ShouldForward(
        string appName,
        string packageName,
        string title,
        string text)
    {
        // 1. 防自循环：过滤本应用自身
        if (FilterSelf && IsSelfNotification(packageName, appName))
            return false;

        // 2. 用户过滤条目检查
        if (_cachedEntries != null && _cachedEnabledIds != null)
        {
            foreach (var entry in _cachedEntries)
            {
                if (!_cachedEnabledIds.Contains(entry.Id))
                    continue;
                if (entry.Matches(packageName, title, text))
                    return false;
            }
        }

        // 3. 空内容过滤
        if (FilterNoTitleOrText && string.IsNullOrWhiteSpace(title) && string.IsNullOrWhiteSpace(text))
            return false;

        return true;
    }

    /// <summary>
    /// 判断是否为本应用自身通知
    /// </summary>
    private static bool IsSelfNotification(string packageName, string appName)
    {
        if (packageName.Contains(SelfPackageIndicator, StringComparison.OrdinalIgnoreCase))
            return true;
        if (appName.Contains(SelfPackageIndicator, StringComparison.OrdinalIgnoreCase))
            return true;
        return false;
    }
}
