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
/// 时序：
/// ```mermaid
/// sequenceDiagram
///     participant Core as Rust Core
///     participant Store as DeviceSnapshotStore
///     participant DS as DiscoveryService
///     participant UI as DiscoveredDevices
///
///     Core-->>Store: on_device_discovered / on_device_timeout（仅通知）
///     Store->>Core: nrc_get_device_list(ctx, 0, 0)
///     Core-->>Store: 已过滤快照（不可见设备 core 不返回）
///     Store->>DS: Refreshed(快照)（UI 线程）
///     DS->>UI: 重建可配对设备列表
/// ```
/// </summary>
public class DiscoveryService(
    ILogger logger,
    IDeviceManager deviceManager,
    IDeviceSnapshotStore snapshotStore
    ) : IDiscoveryService
{
    private readonly DispatcherQueue dispatcher = DispatcherQueue.GetForCurrentThread();
    private LocalDeviceEntity? localDevice;
    private bool isInitialized;

    public ObservableCollection<DiscoveredDevice> DiscoveredDevices { get; } = [];

    public async Task StartDiscoveryAsync()
    {
        try
        {
            await dispatcher.EnqueueAsync(() => DiscoveredDevices.Clear());

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
            await dispatcher.EnqueueAsync(() => DiscoveredDevices.Clear());
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

    /// <summary>
    /// 用 core 快照重建可配对设备列表。在线/离线、可见性、名称/IP/电量全部由 core 判定，
    /// 平台端只负责展示（快照中不可见的设备已由 core 过滤，此处无需再实现过滤策略）。
    /// </summary>
    private void OnSnapshotsRefreshed(IReadOnlyDictionary<string, DeviceSnapshot> snapshots)
    {
        if (!isInitialized) return;

        var visible = new HashSet<string>(StringComparer.Ordinal);

        foreach (var snap in snapshots.Values)
        {
            if (snap.Uuid == localDevice?.DeviceId) continue;
            visible.Add(snap.Uuid);

            var discovered = new DiscoveredDevice(
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
                snap.Paired);

            var existing = DiscoveredDevices.FirstOrDefault(x => x.DeviceId == snap.Uuid);
            if (existing is not null)
            {
                DiscoveredDevices[DiscoveredDevices.IndexOf(existing)] = discovered;
            }
            else
            {
                DiscoveredDevices.Add(discovered);
            }
        }

        // core 判定不再可见的设备（离线且未配对/未登记）从列表移除
        for (var i = DiscoveredDevices.Count - 1; i >= 0; i--)
        {
            if (!visible.Contains(DiscoveredDevices[i].DeviceId))
            {
                DiscoveredDevices.RemoveAt(i);
            }
        }
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
                DiscoveredDevices.Clear();
                isInitialized = false;
            });
        }
        catch (Exception ex)
        {
            logger.LogError("停止发现服务时出错：{message}", ex.Message);
        }
    }
}
