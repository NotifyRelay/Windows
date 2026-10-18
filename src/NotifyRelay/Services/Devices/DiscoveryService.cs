using CommunityToolkit.WinUI;
using Microsoft.UI.Dispatching;
using NotifyRelay.Data.AppDatabase.Models;
using NotifyRelay.Data.Contracts;
using NotifyRelay.Data.Models;
using NotifyRelay.Native;
using NotifyRelay.Services.Infrastructure;

namespace NotifyRelay.Services.Devices;

/// <summary>
/// 设备发现服务：把 Rust core 的设备快照投影为「可配对设备列表」。
///
/// 在线/离线、可见性、名称/IP/电量/配对状态<b>全部由 core 判定</b>，平台端只负责展示：
/// 数据来自 <see cref="IDeviceSnapshotStore"/>（设备状态的唯一真源消费端），
/// 本类不再自行调用 <c>nrc_get_device_list</c>，也不再维护独立刷新链路。
///
/// 集合发布策略：每轮刷新<b>构建新集合并整体替换实例</b>，不做逐项 Add/Remove/Replace。
/// 原因：快照现由心跳驱动（约 500ms 一次），逐项增量通知会让 <c>ItemsRepeater</c>
/// 在布局未完成时反复接收集合变更，原生处理器抛 0x80004005 导致进程崩溃。
///
/// 时序：
/// ```mermaid
/// sequenceDiagram
///     participant Core as Rust Core
///     participant Store as DeviceSnapshotStore
///     participant DS as DiscoveryService
///     participant UI as DiscoveredDevices（新实例）
///
///     Core-->>Store: on_device_discovered / on_device_timeout（仅通知）
///     Store->>Core: nrc_get_device_list(ctx, 0, 0)
///     Core-->>Store: 已过滤快照（不可见设备 core 不返回）
///     Store->>DS: Refreshed(快照)（UI 线程）
///     DS->>DS: 构建新集合
///     DS->>UI: 替换 DiscoveredDevices 实例 + PropertyChanged
/// ```
/// </summary>
public class DiscoveryService(
    ILogger logger,
    IDeviceManager deviceManager,
    IDeviceSnapshotStore snapshotStore
    ) : ObservableObject, IDiscoveryService
{
    private readonly DispatcherQueue dispatcher = DispatcherQueue.GetForCurrentThread();
    private LocalDeviceEntity? localDevice;
    private bool isInitialized;

    private ObservableCollection<DiscoveredDevice> discoveredDevices = [];

    public ObservableCollection<DiscoveredDevice> DiscoveredDevices
    {
        get => discoveredDevices;
        private set => SetProperty(ref discoveredDevices, value);
    }

    public async Task StartDiscoveryAsync()
    {
        try
        {
            await dispatcher.EnqueueAsync(() =>
            {
                DiscoveredDevices = [];
                lastPublished = [];
            });

            localDevice = await deviceManager.GetLocalDeviceAsync();
            logger.LogInformation("本地设备初始化完成：{deviceId}, {deviceName}", localDevice.DeviceId, localDevice.DeviceName);

            deviceManager.LocalDeviceNameChanged += OnLocalDeviceNameChanged;

            var systemInfoService = Ioc.Default.GetService<ISystemInfoService>();
            var batteryLevel = systemInfoService?.GetSystemBatteryLevel() ?? 100;
            var isCharging = systemInfoService?.GetSystemChargingStatus() ?? true;
            var signedBattery = isCharging ? Math.Abs(batteryLevel) : -Math.Abs(batteryLevel);

            // 通过 Rust 内核启动周期性 TCP 扫描发现
            NativeCore.PeriodicBroadcast(1, localDevice.DeviceId, localDevice.DeviceName, signedBattery, "pc");

            // 先置位再订阅：否则首次快照回调会因 isInitialized=false 被丢弃，列表保持为空
            isInitialized = true;

            // 设备状态变化 → 由 DeviceSnapshotStore 统一拉取快照并回调本类重建列表
            snapshotStore.Refreshed -= OnSnapshotsRefreshed;
            snapshotStore.Refreshed += OnSnapshotsRefreshed;
            snapshotStore.Start();
            snapshotStore.RequestRefresh();

            logger.LogInformation("发现服务已完全初始化");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "启动发现服务时出错");
            isInitialized = false;
            await dispatcher.EnqueueAsync(() =>
            {
                DiscoveredDevices = [];
                lastPublished = [];
            });
        }
    }

    private void OnLocalDeviceNameChanged(object? sender, string newName)
    {
        try
        {
            if (localDevice == null) return;
            logger.LogInformation("本地设备名已更改：{newName}", newName);
            localDevice.DeviceName = newName;
            NativeCore.PeriodicBroadcast(2, name: newName);

            var systemInfoService = Ioc.Default.GetService<ISystemInfoService>();
            var batteryLevel = systemInfoService?.GetSystemBatteryLevel() ?? 100;
            var isCharging = systemInfoService?.GetSystemChargingStatus() ?? true;
            var signedBattery = isCharging ? Math.Abs(batteryLevel) : -Math.Abs(batteryLevel);
            NativeCore.UpdateHeartbeatSchedulerParams(newName, signedBattery, "pc");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "处理本地设备名更改时出错");
        }
    }

    /// <summary>上一轮发布的快照（用于内容比对，避免无变化时重建 UI）。</summary>
    private List<DeviceSnapshot> lastPublished = [];

    /// <summary>
    /// 用 core 快照重建可配对设备列表。
    ///
    /// 在线/离线、可见性、名称/IP/电量全部由 core 判定（快照中不可见的设备已由 core 过滤），
    /// 平台端只负责展示；集合整体替换，避免高频增量通知击穿 ItemsRepeater。
    /// 内容与上一轮一致时不替换实例，避免无谓的 UI 重建（快照约 500ms 一轮）。
    /// </summary>
    private void OnSnapshotsRefreshed(IReadOnlyDictionary<string, DeviceSnapshot> snapshots)
    {
        if (!isInitialized) return;

        var visible = snapshots.Values
            .Where(s => s.Uuid != localDevice?.DeviceId)
            .ToList();

        // 比较原始快照字段（而非派生的 DateTimeOffset：LastSeen<=0 时会退化为当前时间，永远不等）
        if (IsSameAsLastPublished(visible)) return;

        var next = new ObservableCollection<DiscoveredDevice>();
        foreach (var snap in visible)
        {
            next.Add(new DiscoveredDevice(
                snap.Uuid,
                null,
                // 名称回退链：core 快照 → uuid→名缓存 → uuid
                // （缓存覆盖设备离线、core 快照名为空的场景，避免显示为 uuid）
                snap.DisplayName(DeviceNameCache.TryGetDisplayName(snap.Uuid)),
                snap.LastSeenTime,
                snap.Port,
                snap.Ip ?? string.Empty,
                snap.Battery,
                snap.DeviceType ?? string.Empty,
                snap.Online,
                snap.Paired));
        }

        lastPublished = visible;
        DiscoveredDevices = next;
    }

    /// <summary>
    /// 判断新快照与上一轮发布内容是否一致（顺序与展示字段全等）。
    ///
    /// 刻意<b>不比较 LastSeen</b>：它有心跳就前进（约 500ms 一次），而三处模板都只渲染
    /// DeviceName，拿它参与比较会导致每轮都重建 UI（闪烁/滚动位置丢失）。
    /// </summary>
    private bool IsSameAsLastPublished(List<DeviceSnapshot> visible)
    {
        if (lastPublished.Count != visible.Count) return false;

        for (var i = 0; i < visible.Count; i++)
        {
            var a = lastPublished[i];
            var b = visible[i];
            if (a.Uuid != b.Uuid
                || a.Name != b.Name
                || a.Ip != b.Ip
                || a.Port != b.Port
                || a.Battery != b.Battery
                || a.DeviceType != b.DeviceType
                || a.Online != b.Online
                || a.Paired != b.Paired)
            {
                return false;
            }
        }

        return true;
    }

    public void StopDiscovery()
    {
        NativeCore.PeriodicBroadcast(0);
        snapshotStore.Refreshed -= OnSnapshotsRefreshed;

        try
        {
            deviceManager.LocalDeviceNameChanged -= OnLocalDeviceNameChanged;
            dispatcher.TryEnqueue(() =>
            {
                DiscoveredDevices = [];
                lastPublished = [];
                isInitialized = false;
            });
        }
        catch (Exception ex)
        {
            logger.LogError("停止发现服务时出错：{message}", ex.Message);
        }
    }
}
