using NotifyRelay.Data.Models;

namespace NotifyRelay.Data.Contracts;

/// <summary>
/// 设备状态的<b>唯一真源消费端</b>：把 Rust core 的 <c>nrc_get_device_list</c> 快照
/// 转换为平台端只读投影。
///
/// 写入约束：<b>只有本接口的实现可以刷新设备状态</b>；其他模块（心跳、网络变化、UI）
/// 只能调用 <see cref="RequestRefresh"/> 触发一次刷新，不得再自行维护设备副本。
///
/// 兜底策略：仅 <see cref="DeviceSnapshot.Name"/> 与 <see cref="DeviceSnapshot.DeviceType"/>
/// 使用「上帧非空值」兜底；其余字段一律以 core 为准。
///
/// 时序：
/// ```mermaid
/// sequenceDiagram
///     participant T as 触发方（Rust 回调 / 心跳 / 定时器）
///     participant S as IDeviceSnapshotStore
///     participant Core as Rust Core
///     participant Sub as DeviceManager / DiscoveryService
///
///     T->>S: RequestRefresh()（异步，不在 core 回调栈内重入）
///     S->>Core: nrc_get_device_list(ctx, 0, 0)
///     Core-->>S: 设备快照 JSON
///     S->>Sub: Refreshed(只读投影)（UI 线程）
/// ```
/// </summary>
public interface IDeviceSnapshotStore
{
    /// <summary>当前快照投影（key = uuid，已排除本机）。</summary>
    IReadOnlyDictionary<string, DeviceSnapshot> Snapshots { get; }

    /// <summary>投影刷新完成（实现方负责切回 UI 线程回调）。</summary>
    event Action<IReadOnlyDictionary<string, DeviceSnapshot>>? Refreshed;

    /// <summary>
    /// Rust core 是否已产出可用快照。
    /// 启动早期（core 尚未就绪）为 false，避免把「还没扫到」误判成「设备已离线」。
    /// </summary>
    bool IsReady { get; }

    /// <summary>本机 uuid（快照中会被排除）。</summary>
    string? LocalUuid { get; }

    /// <summary>按 uuid 取单条快照（不存在返回 null）。</summary>
    DeviceSnapshot? Snapshot(string uuid);

    /// <summary>
    /// 异步触发一次刷新。Rust 回调线程内安全：内部切到线程池执行，
    /// 不会在 core 回调栈内同步重入 <c>nrc_get_device_list</c>。
    /// </summary>
    void RequestRefresh();

    /// <summary>同步刷新（调用方自行确保不在 Rust 回调线程内）。</summary>
    void Refresh();

    /// <summary>清除某设备的兜底缓存与投影（设备被移除时调用）。</summary>
    void Forget(string uuid);

    /// <summary>启动快照刷新（回调触发 + 定时兜底）。</summary>
    void Start();

    /// <summary>停止快照刷新。</summary>
    void Stop();
}
