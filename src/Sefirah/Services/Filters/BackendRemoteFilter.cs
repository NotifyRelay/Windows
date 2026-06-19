using NotifyRelay.Data.Models;

namespace NotifyRelay.Services.Filters;

/// <summary>
/// 远程通知过滤器（Android → PC 方向）
/// 决定 Android 端发来的通知是否在 PC 上显示
/// 功能对齐 Android 端 BackendRemoteFilter：
/// - 包名等价组映射（合一化）
/// - 过滤模式：none / black / white / peer
/// - 文本去重（80% 匹配率）
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
    public bool ShouldBlock(NotificationMessage message)
    {
        return !FilterRemoteNotification(message).ShouldShow;
    }

    /// <summary>
    /// 过滤远程通知
    /// </summary>
    public FilterResult FilterRemoteNotification(NotificationMessage message)
    {
        var pkg = message.AppPackage ?? string.Empty;
        var title = message.Title ?? string.Empty;
        var text = message.Text ?? string.Empty;

        // 1. 包名等价组映射
        var mappedPkg = MapToLocalPackage(pkg);

        // 2. 对等模式过滤：如果本机已安装相同应用，跳过（由本机自己显示）
        if (EnablePeerMode && !string.IsNullOrEmpty(mappedPkg) &&
            InstalledPackageNames.Contains(mappedPkg))
        {
            _logger.LogDebug("对等模式过滤: {pkg} 本机已安装", mappedPkg);
            return FilterResult.Blocked;
        }

        // 3. 过滤模式检查
        if (!CheckFilterMode(mappedPkg, pkg, title, text))
            return FilterResult.Blocked;

        // 4. 文本去重
        if (EnableDeduplication && IsDuplicate(title, text))
        {
            _logger.LogDebug("文本去重过滤: title={title}, text={text}", title, text);
            return FilterResult.Blocked;
        }

        // 5. 记录到去重缓存
        if (EnableDeduplication)
        {
            RecordNotification(title, text);
        }

        return FilterResult.Passed(mappedPkg, message.AppName);
    }

    /// <summary>
    /// 包名等价组映射
    /// </summary>
    public string MapToLocalPackage(string pkg)
    {
        if (!EnablePackageGroupMapping || string.IsNullOrEmpty(pkg))
            return pkg;

        for (int i = 0; i < PackageGroups.Count; i++)
        {
            if (i >= PackageGroupEnabled.Count || !PackageGroupEnabled[i])
                continue;

            var group = PackageGroups[i];
            if (group.Contains(pkg, StringComparer.OrdinalIgnoreCase))
            {
                return group[0]; // 统一返回组内第一个包名
            }
        }

        return pkg;
    }

    /// <summary>
    /// 检查过滤模式
    /// </summary>
    private bool CheckFilterMode(string mappedPkg, string originalPkg, string title, string text)
    {
        if (FilterMode == "none")
            return true;

        if (FilterMode == "black")
        {
            foreach (var entry in FilterList)
            {
                var targetPkg = string.IsNullOrEmpty(entry.PackageName) ? "" : entry.PackageName;
                var pkgMatch = mappedPkg.Equals(targetPkg, StringComparison.OrdinalIgnoreCase) ||
                               originalPkg.Equals(targetPkg, StringComparison.OrdinalIgnoreCase);

                if (!pkgMatch)
                    continue;

                if (string.IsNullOrEmpty(entry.Keyword))
                    return false;

                var lowerKw = entry.Keyword.ToLowerInvariant();
                if (title.ToLowerInvariant().Contains(lowerKw) ||
                    text.ToLowerInvariant().Contains(lowerKw))
                    return false;
            }
            return true;
        }

        if (FilterMode == "white")
        {
            foreach (var entry in FilterList)
            {
                var targetPkg = string.IsNullOrEmpty(entry.PackageName) ? "" : entry.PackageName;
                var pkgMatch = mappedPkg.Equals(targetPkg, StringComparison.OrdinalIgnoreCase) ||
                               originalPkg.Equals(targetPkg, StringComparison.OrdinalIgnoreCase);

                if (!pkgMatch && !string.IsNullOrEmpty(targetPkg))
                    continue;

                if (string.IsNullOrEmpty(entry.Keyword))
                    return true;

                var lowerKw = entry.Keyword.ToLowerInvariant();
                if (title.ToLowerInvariant().Contains(lowerKw) ||
                    text.ToLowerInvariant().Contains(lowerKw))
                    return true;
            }
            return false;
        }

        return true;
    }

    // ====== 文本去重 ======

    /// <summary>
    /// 判断是否与最近通知重复（80%+ 匹配率）
    /// </summary>
    private bool IsDuplicate(string newTitle, string newText)
    {
        CleanupStaleEntries();

        lock (_recentNotifications)
        {
            foreach (var (existingTitle, existingText, _) in _recentNotifications)
            {
                var sim = CalculateCombinedSimilarity(newTitle, newText, existingTitle, existingText);
                if (sim >= 0.8)
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

    /// <summary>
    /// 计算标题和内容的综合相似度（分开计算后合并）
    /// </summary>
    private static double CalculateCombinedSimilarity(
        string newTitle, string newText,
        string oldTitle, string oldText)
    {
        bool titleEmpty = string.IsNullOrWhiteSpace(newTitle) && string.IsNullOrWhiteSpace(oldTitle);
        bool textEmpty = string.IsNullOrWhiteSpace(newText) && string.IsNullOrWhiteSpace(oldText);

        if (titleEmpty && textEmpty)
            return 1.0;

        if (titleEmpty)
            return CalculateTextSimilarity(newText, oldText);

        if (textEmpty)
            return CalculateTextSimilarity(newTitle, oldTitle);

        // 标题和内容各占 50%
        var titleSim = CalculateTextSimilarity(newTitle, oldTitle);
        var textSim = CalculateTextSimilarity(newText, oldText);
        return (titleSim + textSim) / 2.0;
    }

    /// <summary>
    /// 计算两段文本的相似度（基于字符级 Jaccard + 公共子序列）
    /// 返回 0.0 ~ 1.0
    /// </summary>
    private static double CalculateTextSimilarity(string a, string b)
    {
        if (string.IsNullOrEmpty(a) && string.IsNullOrEmpty(b))
            return 1.0;
        if (string.IsNullOrEmpty(a) || string.IsNullOrEmpty(b))
            return 0.0;

        a = a.Trim().ToLowerInvariant();
        b = b.Trim().ToLowerInvariant();

        if (a == b)
            return 1.0;
        if (a.Contains(b) || b.Contains(a))
            return 0.9;

        // 字符级 Jaccard 相似度
        var setA = new HashSet<char>(a);
        var setB = new HashSet<char>(b);

        if (setA.Count == 0 && setB.Count == 0)
            return 1.0;

        var intersection = setA.Intersect(setB).Count();
        var union = setA.Union(setB).Count();

        var jaccard = union > 0 ? (double)intersection / union : 0.0;

        // 长度比惩罚
        var lenRatio = (double)Math.Min(a.Length, b.Length) / Math.Max(a.Length, b.Length);

        // 综合：Jaccard 权重 0.7 + 长度比权重 0.3
        return jaccard * 0.7 + lenRatio * 0.3;
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
