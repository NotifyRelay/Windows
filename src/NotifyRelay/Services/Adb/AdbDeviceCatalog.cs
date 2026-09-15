using AdvancedSharpAdbClient.Models;
using CommunityToolkit.WinUI;
using NotifyRelay.Data.Enums;
using NotifyRelay.Data.Models;

namespace NotifyRelay.Services.Adb;

/// <summary>
/// 设备集合读写契约：按序列号查找、存在性、无线在线判定、增删改、快照、连接匹配。
/// </summary>
public interface IAdbDeviceCatalog
{
    /// <summary>与 <c>IAdbService.AdbDevices</c> 相同的底层集合实例。</summary>
    ObservableCollection<AdbDevice> Devices { get; }

    Task<AdbDevice?> FindBySerialAsync(string serial);
    Task<bool> ExistsAsync(string serial);
    Task<bool> IsWirelessOnlineAsync(string hostIp);
    Task AddAsync(AdbDevice device);

    /// <summary>不存在同序列号设备时才添加；返回是否实际添加。</summary>
    Task<bool> AddIfMissingAsync(AdbDevice device);

    /// <summary>RemoveAt + Insert 替换（触发 CollectionChanged）；返回是否命中并替换。</summary>
    Task<bool> ReplaceAsync(AdbDevice existing, AdbDevice updated);

    /// <summary>就地修改 State 后 RemoveAt + Insert 重插同一实例；返回是否命中。</summary>
    Task<bool> UpdateStateAsync(AdbDevice device, DeviceState newState);

    Task RemoveAsync(AdbDevice device);
    Task ClearAsync();
    Task<IReadOnlyList<AdbDevice>> SnapshotAsync();

    /// <summary>判断已配对设备是否已有在线 ADB 连接（AndroidId 精确匹配，为空时按型号模糊匹配）。</summary>
    Task<bool> HasConnectionForAsync(PairedDevice device);

    /// <summary>通用 UI 线程读取逃生口。</summary>
    Task<T?> ReadAsync<T>(Func<IEnumerable<AdbDevice>, T?> selector);
}

/// <summary>
/// 持有 <see cref="AdbDevices"/> ObservableCollection 的唯一实例，
/// 统一封装所有集合读写的 UI 线程封送，对外提供幂等的增删改查。
/// 不发起任何 ADB 调用；不做设备信息解析；不订阅 DeviceMonitor 事件。
/// </summary>
public sealed class AdbDeviceCatalog : IAdbDeviceCatalog
{
    private readonly ObservableCollection<AdbDevice> devices = [];

    public ObservableCollection<AdbDevice> Devices => devices;

    public Task<AdbDevice?> FindBySerialAsync(string serial)
        => ReadAsync(d => d.FirstOrDefault(x => x.Serial == serial));

    public Task<bool> ExistsAsync(string serial)
        => ReadAsync(d => d.Any(x => x.Serial == serial));

    public Task<bool> IsWirelessOnlineAsync(string hostIp)
        => ReadAsync(d => d.Any(x => x.Serial == $"{hostIp}:5555" && x.IsOnline));

    public Task AddAsync(AdbDevice device)
        => EnqueueAsync(() => devices.Add(device));

    public async Task<bool> AddIfMissingAsync(AdbDevice device)
    {
        bool added = false;
        await EnqueueAsync(() =>
        {
            if (!devices.Any(d => d.Serial == device.Serial))
            {
                devices.Add(device);
                added = true;
            }
        });
        return added;
    }

    public async Task<bool> ReplaceAsync(AdbDevice existing, AdbDevice updated)
    {
        bool replaced = false;
        await EnqueueAsync(() =>
        {
            // Update existing device using Remove + Insert to trigger CollectionChanged
            var index = devices.IndexOf(existing);
            if (index != -1)
            {
                devices.RemoveAt(index);
                devices.Insert(index, updated);
                replaced = true;
            }
        });
        return replaced;
    }

    public async Task<bool> UpdateStateAsync(AdbDevice device, DeviceState newState)
    {
        bool updated = false;
        await EnqueueAsync(() =>
        {
            var index = devices.IndexOf(device);
            if (index != -1)
            {
                // Update using Remove + Insert to trigger CollectionChanged
                device.State = newState;
                devices.RemoveAt(index);
                devices.Insert(index, device);
                updated = true;
            }
        });
        return updated;
    }

    public Task RemoveAsync(AdbDevice device)
        => EnqueueAsync(() =>
        {
            var index = devices.IndexOf(device);
            if (index != -1)
            {
                devices.RemoveAt(index);
            }
        });

    public Task ClearAsync()
        => EnqueueAsync(devices.Clear);

    public async Task<IReadOnlyList<AdbDevice>> SnapshotAsync()
    {
        var snapshot = await ReadAsync<IReadOnlyList<AdbDevice>>(d => d.ToList());
        return snapshot ?? [];
    }

    public Task<bool> HasConnectionForAsync(PairedDevice device)
        => ReadAsync(d => d.Any(adbDevice =>
            adbDevice.IsOnline &&
            (
                (!string.IsNullOrEmpty(adbDevice.AndroidId) && adbDevice.AndroidId == device.Id) ||
                (string.IsNullOrEmpty(adbDevice.AndroidId) &&
                    !string.IsNullOrEmpty(adbDevice.Model) &&
                    !string.IsNullOrEmpty(device.Model) &&
                    (device.Model.Equals(adbDevice.Model, StringComparison.OrdinalIgnoreCase) ||
                     device.Model.Contains(adbDevice.Model, StringComparison.OrdinalIgnoreCase) ||
                     adbDevice.Model.Contains(device.Model, StringComparison.OrdinalIgnoreCase)))
            )));

    public async Task<T?> ReadAsync<T>(Func<IEnumerable<AdbDevice>, T?> selector)
    {
        T? result = default;
        await EnqueueAsync(() =>
        {
            result = selector(devices);
        });
        return result;
    }

    private static Task EnqueueAsync(Action action)
        => App.MainWindow.DispatcherQueue.EnqueueAsync(action);
}
