using NotifyRelay.Data.Models;

namespace NotifyRelay.Data.Contracts;

/// <summary>
/// 设备信息的<b>同步查询</b>入口，全部数据来自 <see cref="IDeviceSnapshotStore"/> 的只读投影。
///
/// 之所以需要它：<c>nrc_get_device_list</c> 是异步快照，而业务侧（通知/图标/应用列表等）
/// 需要在任意线程<b>同步</b>按 uuid 反查设备信息。本接口只读取投影，不写任何状态。
/// </summary>
public interface IDeviceDirectory
{
    /// <summary>按 uuid 反查快照（未知设备返回 null）。</summary>
    DeviceSnapshot? Find(string uuid);

    /// <summary>在线且已配对的设备快照（IP 有效性不做过滤）。</summary>
    IReadOnlyList<DeviceSnapshot> OnlinePaired();

    /// <summary>在线且已配对的设备数量。</summary>
    int OnlinePairedCount();
}
