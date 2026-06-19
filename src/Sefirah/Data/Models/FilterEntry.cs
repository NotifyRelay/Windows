namespace NotifyRelay.Data.Models;

/// <summary>
/// 过滤条目（与 Android 端 BackendLocalFilter.FilterEntry 对齐）
/// keyword 匹配通知标题/内容，packageName 匹配应用名
/// 两者均为空时该条目无效
/// </summary>
public class FilterEntry
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..8];
    public string Keyword { get; set; } = string.Empty;
    public string PackageName { get; set; } = string.Empty;
    public bool Enabled { get; set; } = true;

    public FilterEntry() { }

    public FilterEntry(string keyword, string packageName)
    {
        Keyword = keyword;
        PackageName = packageName;
    }

    /// <summary>
    /// 检查此条目是否匹配给定通知
    /// </summary>
    public bool Matches(string packageName, string title, string text)
    {
        bool keywordMatch = string.IsNullOrWhiteSpace(Keyword);
        if (!keywordMatch)
        {
            var lowerKw = Keyword.ToLowerInvariant();
            keywordMatch = title.ToLowerInvariant().Contains(lowerKw) ||
                           text.ToLowerInvariant().Contains(lowerKw);
        }

        bool packageMatch = string.IsNullOrWhiteSpace(PackageName);
        if (!packageMatch)
        {
            packageMatch = packageName.Equals(PackageName, StringComparison.OrdinalIgnoreCase) ||
                           packageName.Contains(PackageName, StringComparison.OrdinalIgnoreCase);
        }

        // 两者都非空时需要同时匹配；只有 keyword 则只匹配 title/text；只有 packageName 则只匹配包名
        if (!string.IsNullOrWhiteSpace(Keyword) && !string.IsNullOrWhiteSpace(PackageName))
            return keywordMatch && packageMatch;
        if (!string.IsNullOrWhiteSpace(Keyword))
            return keywordMatch;
        if (!string.IsNullOrWhiteSpace(PackageName))
            return packageMatch;

        return false;
    }

    public override bool Equals(object? obj) =>
        obj is FilterEntry other && Id == other.Id;

    public override int GetHashCode() => Id.GetHashCode();

    public override string ToString()
    {
        if (!string.IsNullOrWhiteSpace(Keyword) && !string.IsNullOrWhiteSpace(PackageName))
            return $"{Keyword} | {PackageName}";
        if (!string.IsNullOrWhiteSpace(Keyword))
            return Keyword;
        if (!string.IsNullOrWhiteSpace(PackageName))
            return PackageName;
        return "(空)";
    }
}

/// <summary>
/// 远程过滤列表条目（包名 + 可选关键词）
/// </summary>
public class FilterListEntry
{
    public string PackageName { get; set; } = string.Empty;
    public string? Keyword { get; set; }
}
