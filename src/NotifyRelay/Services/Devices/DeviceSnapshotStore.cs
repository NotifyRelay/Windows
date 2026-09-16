using System.Text.Json;
using CommunityToolkit.WinUI;
using Microsoft.UI.Dispatching;
using NotifyRelay.Data.Contracts;
using NotifyRelay.Data.Models;
using NotifyRelay.Native;

namespace NotifyRelay.Services.Devices;

/// <summary>
/// 设备状态的<b>唯一真源消费端</b>：把 Rust core 的 <c>nrc_get_device_list</c> 快照
/// 转换为平台端只读投影，并在刷新完成后通知订阅方（<see cref="DeviceManager"/>、
/// <see cref="DiscoveryService"/>）重建各自的展示集合。
///
/// 写入约束：只有本类可以刷新设备状态；其他模块只能调用 <see cref="RequestRefresh"/>
/// 触发一次刷新，不得再自行维护设备副本（在线/离线/名称/IP/电量一律以 core 为准）。
///
/// 兜底策略：仅 <see cref="DeviceSnapshot.Name"/> 与 <see cref="DeviceSnapshot.DeviceType"/>
/// 使用「上帧非空值」兜底（core 的 deviceType 不落库、name 重启首帧可能为空）；其余字段一律以 core 为准。
///
/// 时序：
/// ```mermaid
/// sequenceDiagram
///     participant T as 触发方（Rust 回调 / 心跳 / 定时器）
///     participant S as DeviceSnapshotStore
///     participant Core as Rust Core
///     participant Sub as DeviceManager / DiscoveryService
///
///     T->>S: RequestRefresh()（或定时器 Tick）
///     S->>S: refreshBusy 原子闸门（并发触发直接跳过）
///     S->>Core: nrc_get_device_list(ctx, 0, 0)
///     Core-->>S: [{uuid,name,ip,port,battery,deviceType,online,paired,...}]
///     S->>S: 解析 + name/deviceType 上帧兜底 → 更新只读投影
///     S->>Sub: Refreshed(投影)（UI 线程）
/// ```
/// </summary>
public sealed class DeviceSnapshotStore(
    ILogger<DeviceSnapshotStore> logger,
    HeartbeatProcessor heartbeatProcessor) : IDeviceSnapshotStore
{
    /// <summary>
    /// 快照兜底刷新间隔（与 Android 端保持一致）：
    /// 保证设备离线后即使没有回调也能从列表中移除。
    /// </summary>
    private static readonly TimeSpan RefreshInterval = TimeSpan.FromSeconds(5);

    /// <summary>name/deviceType 兜底缓存容量上限</summary>
    private const int FallbackMaxEntries = 500;

    /// <summary>
    /// 发布快照用的 UI 调度队列。
    ///
    /// 必须归属 UI 线程：快照回调会驱动 <c>PairedDevices</c> / <c>DiscoveredDevices</c> 等
    /// 绑定集合的变更，若在后台线程发布会由原生集合变更处理器抛出 0x80004005。
    /// 构造时通常已在 UI 线程；兜底取主窗口队列，避免服务在后台线程被首次解析时丢失归属。
    /// </summary>
    private readonly DispatcherQueue? dispatcher =
        DispatcherQueue.GetForCurrentThread() ?? App.MainWindow?.DispatcherQueue;

    /// <summary>core 快照的只读投影（刷新在后台线程写，查询可能在任意线程读）。</summary>
    private volatile Dictionary<string, DeviceSnapshot> projection = [];

    /// <summary>name/deviceType 的上帧兜底（core 重启首帧可能为空）；按插入顺序淘汰最旧条目。</summary>
    private readonly Dictionary<string, (string Name, string DeviceType)> displayFallback = [];

    /// <summary>刷新闸门：心跳/回调/定时器并发触发时跳过重复刷新，避免同时进入 core。</summary>
    private int refreshBusy;

    /// <summary>刷新轮次序号：用于与 <see cref="forgotten"/> 比较，判定读到的快照是否为删除前的旧数据。</summary>
    private int refreshSeq;

    /// <summary>
    /// 已遗忘设备的墓碑（uuid → 遗忘发生时的轮次序号）。
    ///
    /// 设备被显式移除后，core 侧删除尚未完全生效时返回的快照可能仍包含该设备；
    /// 记录墓碑可阻止「旧快照把已删设备写回列表」。
    /// </summary>
    private readonly Dictionary<string, int> forgotten = [];

    private DispatcherQueueTimer? refreshTimer;
    private bool started;

    public event Action<IReadOnlyDictionary<string, DeviceSnapshot>>? Refreshed;

    public IReadOnlyDictionary<string, DeviceSnapshot> Snapshots => projection;

    /// <summary>本机 uuid：快照中会被排除（core 也会排除，双保险）。</summary>
    public string? LocalUuid { get; private set; }

    public bool IsReady { get; private set; }

    public DeviceSnapshot? Snapshot(string uuid)
        => projection.TryGetValue(uuid, out var snap) ? snap : null;

    // ==================== 生命周期 ====================

    public void Start()
    {
        if (started) return;
        started = true;

        // Rust 回调（扫描发现 / 设备超时）→ 重新拉取 core 快照
        heartbeatProcessor.DeviceListChanged += OnDeviceListChanged;

        // 定时兜底：保证设备离线后即使没有回调也能从列表中移除
        dispatcher?.TryEnqueue(() =>
        {
            refreshTimer ??= dispatcher.CreateTimer();
            refreshTimer.Interval = RefreshInterval;
            refreshTimer.Tick += OnRefreshTimerTick;
            refreshTimer.Start();
        });

        Refresh();
    }

    public void Stop()
    {
        if (!started) return;
        started = false;

        heartbeatProcessor.DeviceListChanged -= OnDeviceListChanged;

        dispatcher?.TryEnqueue(() =>
        {
            if (refreshTimer is null) return;
            refreshTimer.Tick -= OnRefreshTimerTick;
            refreshTimer.Stop();
        });
    }

    // ==================== 刷新入口 ====================

    private void OnDeviceListChanged()
    {
        // 回调来自 Rust 线程：必须异步刷新，禁止在 core 回调栈内同步重入
        RequestRefresh();
    }

    private void OnRefreshTimerTick(object? sender, object e) => RequestRefresh();

    public void RequestRefresh()
    {
        // 回调线程安全：切到线程池执行，避免在 Rust 回调栈内同步调用 core 造成重入
        _ = Task.Run(Refresh);
    }

    public void Refresh()
    {
        // 原子闸门：多路调用可能并发进入，仅一个刷新可执行
        if (Interlocked.CompareExchange(ref refreshBusy, 1, 0) != 0) return;
        try
        {
            DoRefresh();
        }
        finally
        {
            Interlocked.Exchange(ref refreshBusy, 0);
        }
    }

    // ==================== 内部实现 ====================

    private void DoRefresh()
    {
        try
        {
            // 本轮序号：与墓碑比较，判定读到的数据是否为「遗忘之前」的旧快照
            var seq = Interlocked.Increment(ref refreshSeq);

            var json = NativeCore.GetDeviceList();
            if (string.IsNullOrEmpty(json)) return;

            var parsed = JsonSerializer.Deserialize<List<DeviceSnapshot>>(json);
            if (parsed is null) return;

            LocalUuid = NativeCore.GetLocalUuid() ?? LocalUuid;
            var localUuid = LocalUuid;

            var next = new Dictionary<string, DeviceSnapshot>();
            foreach (var raw in parsed)
            {
                if (string.IsNullOrEmpty(raw.Uuid) || raw.Uuid == localUuid) continue;

                var snap = ApplyDisplayFallback(raw);
                next[raw.Uuid] = snap;

                // 快照带回的名称写入 uuid→名全局缓存：设备离线后 core 快照名称可能为空，
                // 届时由该缓存兜底显示（与 Android DeviceNameCache 语义一致，仅展示用途）
                DeviceNameCache.Update(snap.Uuid, snap.Name);
            }

            lock (forgotten)
            {
                // 墓碑过期：本轮读取严格晚于「遗忘」，core 侧删除必已生效，其状态可权威采信
                if (forgotten.Count > 0)
                {
                    foreach (var uuid in forgotten.Where(kv => kv.Value < seq).Select(kv => kv.Key).ToList())
                    {
                        forgotten.Remove(uuid);
                    }
                }

                // 仍在有效期内的墓碑一律剔除：本次读取可能早于删除，不得把已删设备写回列表
                foreach (var uuid in forgotten.Keys)
                {
                    next.Remove(uuid);
                }

                projection = next;
            }

            // 首个可解析快照即视为 core 就绪：此后「不在列表 / online=false」才代表真实离线
            IsReady = true;

            PublishRefreshed(next);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "刷新 core 设备快照时出错");
        }
    }

    /// <summary>name/deviceType 上帧兜底：仅在 core 本轮为空/未知时沿用上一帧。</summary>
    private DeviceSnapshot ApplyDisplayFallback(DeviceSnapshot raw)
    {
        string name;
        string deviceType;
        lock (displayFallback)
        {
            displayFallback.TryGetValue(raw.Uuid, out var prev);

            deviceType = raw.HasKnownDeviceType
                ? raw.DeviceType
                : (!string.IsNullOrWhiteSpace(prev.DeviceType) ? prev.DeviceType : raw.DeviceType);

            name = !string.IsNullOrWhiteSpace(raw.Name)
                ? raw.Name
                : (prev.Name ?? string.Empty);

            if (!string.IsNullOrWhiteSpace(name) || !string.IsNullOrWhiteSpace(deviceType))
            {
                // 先移除再插入：Dictionary 的重复赋值不改变既有键顺序，
                // 否则「淘汰最旧」可能删掉刚写入的这一条
                displayFallback.Remove(raw.Uuid);
                displayFallback[raw.Uuid] = (name, deviceType);
                while (displayFallback.Count > FallbackMaxEntries)
                {
                    displayFallback.Remove(displayFallback.Keys.First());
                }
            }
        }

        // 兜底值与原值一致时直接复用（避免无意义的新对象）
        if (name == raw.Name && deviceType == raw.DeviceType) return raw;

        return new DeviceSnapshot
        {
            Uuid = raw.Uuid,
            Name = name,
            Ip = raw.Ip,
            Port = raw.Port,
            Battery = raw.Battery,
            DeviceType = deviceType,
            LastSeen = raw.LastSeen,
            Connected = raw.Connected,
            Paired = raw.Paired,
            Online = raw.Online,
        };
    }

    /// <summary>在 UI 线程发布刷新结果，订阅方可安全更新绑定集合。</summary>
    private void PublishRefreshed(Dictionary<string, DeviceSnapshot> snapshot)
    {
        if (dispatcher is null || dispatcher.HasThreadAccess)
        {
            Refreshed?.Invoke(snapshot);
            return;
        }

        dispatcher.TryEnqueue(() => Refreshed?.Invoke(snapshot));
    }

    /// <summary>
    /// 清除某设备的兜底缓存、投影与墓碑（设备被移除时调用）。
    ///
    /// 必须主动发布新投影：<see cref="DiscoveryService"/> 仅通过 <see cref="Refreshed"/>
    /// 更新列表，不发布会让已删设备残留在 UI 上直到下一次心跳/定时刷新。
    /// </summary>
    public void Forget(string uuid)
    {
        lock (displayFallback) { displayFallback.Remove(uuid); }

        Dictionary<string, DeviceSnapshot>? published = null;
        lock (forgotten)
        {
            // 记录墓碑：本轮（及更早开始的）刷新若读到删除前的旧快照，不得把该设备写回
            forgotten[uuid] = Volatile.Read(ref refreshSeq);

            var next = new Dictionary<string, DeviceSnapshot>(projection);
            if (next.Remove(uuid))
            {
                projection = next;
                published = next;
            }
        }

        if (published is not null)
        {
            PublishRefreshed(published);
        }
    }
}
