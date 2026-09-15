using NotifyRelay.Data.Contracts;
using NotifyRelay.Data.Models;

namespace NotifyRelay.Services;

/// <summary>
/// 设备信息的<b>同步查询</b>入口，全部数据来自 <see cref="IDeviceSnapshotStore"/> 的只读投影。
///
/// 本类只读取投影，不写任何状态，也不触发刷新。
/// </summary>
public sealed class DeviceDirectory(IDeviceSnapshotStore store) : IDeviceDirectory
{
    public DeviceSnapshot? Find(string uuid)
        => string.IsNullOrEmpty(uuid) ? null : store.Snapshot(uuid);

    public IReadOnlyList<DeviceSnapshot> OnlinePaired() =>
        store.Snapshots.Values
            .Where(s => s.Online && s.Paired)
            .OrderBy(s => s.Uuid, StringComparer.Ordinal)
            .ToList();

    public int OnlinePairedCount() => store.Snapshots.Values.Count(s => s.Online && s.Paired);
}
