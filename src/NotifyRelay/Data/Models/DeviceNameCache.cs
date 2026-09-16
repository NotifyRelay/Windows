namespace NotifyRelay.Data.Models;

/// <summary>
/// uuid → displayName 的全局只读缓存，供 UI 与业务层按 uuid 反查显示名。
///
/// 缓存为纯展示用途的<b>弱一致副本</b>：core 快照中的名称为空时用它兜底（如设备离线、
/// core 重启首帧名称为空），<b>不参与任何连接/认证判定</b>。
///
/// 时序（与 core 快照的关系）：
/// ```mermaid
/// sequenceDiagram
///     participant Core as Rust Core
///     participant DM as DeviceManager
///     participant C as DeviceNameCache
///     participant UI as UI/业务
///
///     Core-->>DM: 快照（name 可能为空）
///     DM->>C: Update(uuid, name)（name 非空时）
///     UI->>C: GetDisplayName(uuid)
///     C-->>UI: 缓存名 / uuid 兜底
/// ```
/// </summary>
public static class DeviceNameCache
{
    private const int CacheMaxEntries = 500;

    private static readonly Lock Gate = new();
    private static readonly Dictionary<string, string> Names = [];

    /// <summary>记录 uuid → 显示名（名称为空时不写入）。</summary>
    public static void Update(string? uuid, string? displayName)
    {
        if (string.IsNullOrWhiteSpace(uuid) || string.IsNullOrWhiteSpace(displayName)) return;

        lock (Gate)
        {
            // 先移除再插入：保持「最旧优先淘汰」的顺序语义
            Names.Remove(uuid);
            Names[uuid] = displayName;
            while (Names.Count > CacheMaxEntries)
            {
                Names.Remove(Names.Keys.First());
            }
        }
    }

    /// <summary>按 uuid 反查显示名；未知时返回 uuid（与 Android 端语义一致）。</summary>
    public static string GetDisplayName(string? uuid)
    {
        if (string.IsNullOrEmpty(uuid)) return "未知设备";

        lock (Gate)
        {
            return Names.TryGetValue(uuid, out var name) ? name : uuid;
        }
    }

    /// <summary>按 uuid 反查显示名，未命中返回 null（供调用方继续走自身兜底链）。</summary>
    public static string? TryGetDisplayName(string? uuid)
    {
        if (string.IsNullOrEmpty(uuid)) return null;

        lock (Gate)
        {
            return Names.TryGetValue(uuid, out var name) && !string.IsNullOrWhiteSpace(name) ? name : null;
        }
    }

    /// <summary>清除某设备的缓存（设备被移除时调用）。</summary>
    public static void Forget(string? uuid)
    {
        if (string.IsNullOrEmpty(uuid)) return;

        lock (Gate)
        {
            Names.Remove(uuid);
        }
    }
}
