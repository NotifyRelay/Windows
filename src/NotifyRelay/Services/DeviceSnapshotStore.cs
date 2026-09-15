using System.Text.Json;
using CommunityToolkit.WinUI;
using Microsoft.UI.Dispatching;
using NotifyRelay.Data.Contracts;
using NotifyRelay.Data.Models;
using NotifyRelay.Native;

namespace NotifyRelay.Services;

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

    private readonly DispatcherQueue? dispatcher = DispatcherQueue.GetForCurrentThread();

    /// <summary>core 快照的只读投影（刷新在后台线程写，查询可能在任意线程读）。</summary>
    private volatile Dictionary<string, DeviceSnapshot> projection = [];

    /// <summary>name/deviceType 的上帧兜底（core 重启首帧可能为空）；按插入顺序淘汰最旧条目。</summary>
    private readonly Dictionary<string, (string Name, string DeviceType)> displayFallback = [];

    /// <summary>刷新闸门：心跳/回调/定时器并发触发时跳过重复刷新，避免同时进入 core。</summary>
    private int refreshBusy;

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
                next[raw.Uuid] = ApplyDisplayFallback(raw);
            }

            projection = next;
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

    public void Forget(string uuid)
    {
        lock (displayFallback) { displayFallback.Remove(uuid); }

        var next = new Dictionary<string, DeviceSnapshot>(projection);
        if (next.Remove(uuid))
        {
            projection = next;
        }
    }
}
