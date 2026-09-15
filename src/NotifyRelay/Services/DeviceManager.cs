using System.Text;
using CommunityToolkit.WinUI;
using NotifyRelay.Data.AppDatabase.Models;
using NotifyRelay.Data.AppDatabase.Repository;
using NotifyRelay.Data.Contracts;
using NotifyRelay.Data.Models;
using NotifyRelay.Helpers;
using NotifyRelay.Native;
using NotifyRelay.Utils;
namespace NotifyRelay.Services;

public partial class DeviceManager(
    ILogger<DeviceManager> logger,
    DeviceRepository repository,
    IDeviceSnapshotStore snapshotStore) : ObservableObject, IDeviceManager
{
    public ObservableCollection<PairedDevice> PairedDevices { get; set; } = [];

    [ObservableProperty]
    public partial PairedDevice? ActiveDevice { get; set; }

    /// <summary>
    /// Event fired when the active session changes
    /// </summary>

    /// <summary>
    /// Event fired when the local device name changes
    /// </summary>
    public event EventHandler<string>? LocalDeviceNameChanged;

    /// <summary>
    /// 本机 uuid：core 快照中同样会被排除，此处双保险避免自我配对记录进入列表。
    /// </summary>
    private string? localDeviceId;

    /// <summary>
    /// Finds a device session by device ID
    /// </summary>
    public PairedDevice? FindDeviceById(string deviceId)
    {
        return PairedDevices.FirstOrDefault(device => device.Id == deviceId);
    }

    /// <summary>
    /// 订阅 core 快照：列表成员（core 判定 paired）与运行时状态（在线/电量/名称/IP）
    /// 全部以快照为准，平台端不再自行维护。
    ///
    /// 时序：
    /// ```mermaid
    /// sequenceDiagram
    ///     participant Core as Rust Core
    ///     participant Store as DeviceSnapshotStore
    ///     participant DM as DeviceManager
    ///     participant UI as PairedDevices/UI
    ///
    ///     Core-->>Store: on_device_discovered / timeout（仅通知）
    ///     Store->>Core: nrc_get_device_list(ctx, 0, 0)
    ///     Core-->>Store: 快照 [{uuid, paired, online, battery, name, ip, ...}]
    ///     Store->>DM: Refreshed(快照)（UI 线程）
    ///     DM->>DM: 增补 paired 设备（配置从平台库装载）
    ///     DM->>DM: 移除 paired=false 设备（仅内存，不删库）
    ///     DM->>UI: ApplySnapshot 回填在线/电量/名称/IP
    /// ```
    /// </summary>
    private void OnSnapshotsRefreshed(IReadOnlyDictionary<string, DeviceSnapshot> snapshots)
    {
        try
        {
            // core 尚未产出可用快照前不做增删：避免启动早期把「还没扫到」误判为「已解绑」
            if (!snapshotStore.IsReady) return;

            var paired = snapshots.Values.Where(s => s.Paired).ToList();
            var pairedIds = paired.Select(s => s.Uuid).ToHashSet(StringComparer.Ordinal);

            // 1. 补齐 core 判定为已配对、平台列表尚未持有的设备（配置从库装载）
            foreach (var snap in paired)
            {
                if (snap.Uuid == localDeviceId) continue;
                if (FindDeviceById(snap.Uuid) is not null) continue;
                AddFromRepository(snap.Uuid);
            }

            // 2. 移除 core **明确判定为未配对**的设备（快照中存在且 paired=false）：
            //    这类设备的密钥不在 core（旧版不兼容密钥未迁移成功，或已被解绑）。
            //    注意：只处理「core 明确报告未配对」的设备，绝不因设备在快照中「缺席」而移除 ——
            //    持久化加载瞬时失败时 core 会返回空列表，据缺席移除会误清空整个设备列表。
            //    （显式删除设备由 RemoveDevice 路径处理，不依赖此处。）
            for (var i = PairedDevices.Count - 1; i >= 0; i--)
            {
                var id = PairedDevices[i].Id;
                if (id == localDeviceId || pairedIds.Contains(id)) continue;
                if (!snapshots.TryGetValue(id, out var snap) || snap.Paired) continue;

                logger.LogWarning("设备 {deviceId} 未被 core 判定为已配对，移出设备列表", id);
                PairedDevices.RemoveAt(i);
            }

            // 3. 回填运行时状态（在线/电量/名称/IP/最后可见）
            foreach (var snap in paired)
            {
                var device = FindDeviceById(snap.Uuid);
                if (device is null) continue;

                var previousName = device.Name;
                device.ApplySnapshot(snap);

                // 名称持久化：core 快照带回了新名称时落库 + 写 uuid→名缓存，
                // 保证设备离线（快照 name 为空）后仍能显示正确名称而非 uuid。
                if (!string.IsNullOrWhiteSpace(snap.Name) && snap.Name != previousName)
                {
                    PersistDeviceName(device);
                }
            }

            // 4. 活跃设备失效兜底
            if (ActiveDevice is not null && FindDeviceById(ActiveDevice.Id) is null)
            {
                ActiveDevice = PairedDevices.FirstOrDefault();
            }
            else if (ActiveDevice is null && PairedDevices.Count > 0)
            {
                ActiveDevice = PairedDevices.FirstOrDefault();
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "应用 core 设备快照到设备列表时出错");
        }
    }

    /// <summary>
    /// 持久化设备名：core 私有库行（display_name）+ 平台库单列 + uuid→名缓存。
    ///
    /// 名称随心跳变化且离线后 core 快照可能为空，因此必须落库：
    /// - core 库行（<c>nrc_rename_device</c>）：使 core 快照对离线设备也返回名称（与 Android 一致）；
    /// - 平台库：配置载体，保证平台侧不依赖 core 也能显示名称；
    /// - 缓存：覆盖设备尚未进入平台列表（无库行）的早期阶段。
    /// </summary>
    private void PersistDeviceName(PairedDevice device)
    {
        if (string.IsNullOrWhiteSpace(device.Name)) return;

        DeviceNameCache.Update(device.Id, device.Name);

        try
        {
            NativeCore.RenameDevice(device.Id, device.Name);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "core 侧持久化设备名失败 {deviceId}", device.Id);
        }

        try
        {
            repository.UpdateDeviceName(device.Id, device.Name);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "持久化设备名失败 {deviceId}", device.Id);
        }
    }

    /// <summary>
    /// 从平台库装载单台设备的配置（名称/型号/IP/壁纸/ftp 标记），不存在则新建空记录。
    /// 壁纸解码为异步，先加入列表再后台回填，避免阻塞快照刷新（本方法在 UI 线程执行）。
    /// </summary>
    private void AddFromRepository(string deviceId)
    {
        PairedDevice device;
        byte[]? wallpaperBytes = null;

        try
        {
            if (repository.HasDevice(deviceId, out var entity))
            {
                device = new PairedDevice(deviceId)
                {
                    Name = entity.Name,
                    Model = entity.Model,
                    IpAddresses = entity.IpAddresses,
                    RemotePublicKey = entity.PublicKey,
                    HasSentftpRequest = entity.HasSentftpRequest,
                    // SharedSecret 由 Rust 私有库持有，此处不外泄
                };
                wallpaperBytes = entity.WallpaperBytes;
            }
            else
            {
                device = new PairedDevice(deviceId);
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "从平台库装载设备 {deviceId} 失败，使用空记录", deviceId);
            device = new PairedDevice(deviceId);
        }

        PairedDevices.Add(device);

        // 平台库中的名称进入 uuid→名缓存：core 快照名称为空（设备离线）时作为兜底显示
        DeviceNameCache.Update(deviceId, device.Name);

        // 壁纸异步解码后回填（失败静默，不影响设备可用性）
        if (wallpaperBytes is not null)
        {
            _ = LoadWallpaperAsync(device, wallpaperBytes);
        }
    }

    /// <summary>异步解码壁纸并回填（在 UI 线程发起，await 后自动回到 UI 线程赋值）。</summary>
    private static async Task LoadWallpaperAsync(PairedDevice device, byte[] bytes)
    {
        try
        {
            var bitmap = await ImageHelper.ToBitmapAsync(bytes);
            if (bitmap is not null) device.Wallpaper = bitmap;
        }
        catch
        {
            // 壁纸失败不影响设备可用性
        }
    }

    /// <summary>
    /// Updates an existing device in the collection or adds it if it doesn't exist.
    /// Returns the live instance stored in the collection for further updates.
    /// </summary>
    public async Task<PairedDevice> UpdateOrAddDeviceAsync(PairedDevice device, Action<PairedDevice>? updateAction = null)
    {
        var tcs = new TaskCompletionSource<PairedDevice>();

        await App.MainWindow.DispatcherQueue.EnqueueAsync(() =>
        {
            var existingDevice = PairedDevices.FirstOrDefault(d => d.Id == device.Id);
            if (existingDevice is not null)
            {
                existingDevice.Name = device.Name;
                existingDevice.Model = device.Model;
                existingDevice.IpAddresses = device.IpAddresses;
                existingDevice.Wallpaper = device.Wallpaper;
                existingDevice.Session = device.Session;
                existingDevice.RemotePublicKey = device.RemotePublicKey;
                updateAction?.Invoke(existingDevice);
                tcs.SetResult(existingDevice);
            }
            else
            {
                PairedDevices.Add(device);
                updateAction?.Invoke(device);
                tcs.SetResult(device);
            }
        });

        return await tcs.Task;
    }

    public List<string> GetRemoteDeviceIpAddresses()
    {
        return repository.GetRemoteDeviceIpAddresses();
    }

    public async Task<PairedDevice?> GetLastConnectedDevice()
    {
        return await repository.GetLastConnectedDevice();
    }

    public bool RemoveDevice(PairedDevice device)
    {
        logger.LogInformation("RemoveDevice: 开始移除设备 {deviceId} {deviceName}", device.Id, device.Name);
        // Rust 持久化删除（内存/库行/密钥状态），失败时中止平台侧清理：
        // 否则平台记录已删而 Rust 库仍持有旧密钥，重启后设备复活造成两端不一致
        if (NativeCore.RemoveDevice(device.Id) != 0)
        {
            logger.LogError("RemoveDevice: Rust 持久化删除失败 {deviceId}，中止平台侧清理", device.Id);
            return false;
        }
        NativeCore.RemoveKnownDevice(device.Id);
        NativeCore.RemoveDeviceSession(device.Id);
        snapshotStore.Forget(device.Id);
        DeviceNameCache.Forget(device.Id);

        App.MainWindow.DispatcherQueue.EnqueueAsync(() =>
        {
            try
            {
                var existing = PairedDevices.FirstOrDefault(d => d.Id == device.Id);
                if (existing is null)
                {
                    logger.LogWarning("RemoveDevice: PairedDevices 中未找到设备 {deviceId}", device.Id);
                    return;
                }

                PairedDevices.Remove(existing);
                repository.DeletePairedDevice(existing.Id);
                logger.LogInformation("RemoveDevice: 设备 {deviceId} 已从内存和数据库移除", device.Id);

                if (ActiveDevice?.Id == existing.Id)
                {
                    ActiveDevice = PairedDevices.FirstOrDefault();
                }
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "移除设备 {id} 时出错", device.Id);
            }
        });
        return true;
    }

    public void SaveDevice(PairedDevice device)
    {
        var entity = new RemoteDeviceEntity
        {
            DeviceId = device.Id,
            Name = device.Name,
            Model = device.Model,
            IpAddresses = device.IpAddresses ?? [],
            PublicKey = device.RemotePublicKey,
            HasSentftpRequest = device.HasSentftpRequest,
        };
        repository.AddOrUpdateRemoteDevice(entity);
    }

    public async Task<PairedDevice?> VerifyHandshakeAsync(string deviceId, string remotePublicKey, string? deviceName, string? ipAddress)
    {
        try
        {
            var keyJson = NativeCore.ExportDeviceKey(deviceId);
            if (keyJson == null)
            {
                logger.LogError("导出设备密钥失败: {deviceId}", deviceId);
                return null;
            }

            if (repository.HasDevice(deviceId, out var existingDevice))
            {
                existingDevice.LastConnected = DateTime.Now;
                existingDevice.Name = deviceName ?? existingDevice.Name;
                existingDevice.PublicKey = remotePublicKey;

                if (ipAddress is not null && !existingDevice.IpAddresses.Contains(ipAddress))
                {
                    existingDevice.IpAddresses = [.. existingDevice.IpAddresses, ipAddress];
                }

                repository.AddOrUpdateRemoteDevice(existingDevice);

                var pairedDevice = await App.MainWindow.DispatcherQueue.EnqueueAsync(() => existingDevice.ToPairedDevice());
                return pairedDevice;
            }

            logger.LogWarning("未知设备尝试通过 HANDSHAKE 连接，拒绝: {deviceId}", deviceId);
            return null;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "验证设备时出错");
            return null;
        }
    }

    public async Task<LocalDeviceEntity> GetLocalDeviceAsync()
    {
        try
        {
            LocalDeviceEntity? localDevice = null;
            int retryCount = 0;
            const int maxRetries = 3;

            while (localDevice is null && retryCount < maxRetries)
            {
                localDevice = repository.GetLocalDevice();
                if (localDevice is null)
                {
                    retryCount++;
                    await Task.Delay(100);
                }
            }

            if (localDevice is null)
            {
                var (name, _) = await UserInformation.GetCurrentUserInfoAsync();
                NativeCore.GenerateKeypair();
                var publicKeyBase64 = NativeCore.GetPublicKey();
                // 本机 UUID 由 Rust 生成/持有（库落盘），平台端仅读取；Guid 仅异常兜底
                var deviceId = NativeCore.GetLocalUuid() ?? Guid.NewGuid().ToString();
                localDevice = new LocalDeviceEntity
                {
                    DeviceId = deviceId,
                    DeviceName = name,
                    PublicKey = Encoding.UTF8.GetBytes(publicKeyBase64 ?? string.Empty),
                    StateJson = string.Empty, // 密钥状态由 Rust 私有库持有，平台端零存储
                };
                repository.AddOrUpdateLocalDevice(localDevice);

                var savedDevice = repository.GetLocalDevice();
                if (savedDevice is null || savedDevice.DeviceId != localDevice.DeviceId)
                {
                    logger.LogError("保存本地设备失败，UUID可能会在下次启动时重新生成");
                }
            }
            else
            {
                var rustPubKey = NativeCore.GetPublicKey();
                if (rustPubKey == null)
                {
                    // 旧平台加密状态 blob（迁移源）：解密后导入 Rust 内存，
                    // 由 uuid 进入核心后的落盘校验负责清理 StateJson（见 AppLifecycleHelper）
                    bool stateRestored = false;
                    if (!string.IsNullOrEmpty(localDevice.StateJson))
                    {
                        try
                        {
                            var decrypted = NativeCore.DecryptLocalState(localDevice.StateJson, localDevice.DeviceId);
                            if (decrypted != null && NativeCore.ImportState(decrypted) == 0)
                            {
                                rustPubKey = NativeCore.GetPublicKey();
                                var cachedPubKey = Encoding.UTF8.GetString(localDevice.PublicKey ?? []);
                                if (rustPubKey != null && rustPubKey == cachedPubKey)
                                    stateRestored = true;
                            }
                        }
                        catch { }
                    }

                    if (!stateRestored)
                    {
                        logger.LogWarning("本地密钥状态未找到或已损坏，正在生成新密钥对。现有配对的设备需要重新配对。");
                        NativeCore.GenerateKeypair();
                        rustPubKey = NativeCore.GetPublicKey();
                        if (rustPubKey != null)
                            localDevice.PublicKey = Encoding.UTF8.GetBytes(rustPubKey);
                        repository.AddOrUpdateLocalDevice(localDevice);
                    }
                }
                else
                {
                    var cachedPubKey = Encoding.UTF8.GetString(localDevice.PublicKey ?? []);
                    if (rustPubKey != cachedPubKey)
                    {
                        localDevice.PublicKey = Encoding.UTF8.GetBytes(rustPubKey);
                        repository.AddOrUpdateLocalDevice(localDevice);
                    }
                }
            }

            return localDevice;
        }
        catch (Exception e)
        {
            logger.LogError(e, "获取本地设备时出错");
            throw;
        }
    }

    public void UpdateLocalDevice(LocalDeviceEntity device)
    {
        try
        {
            var existingDevice = repository.GetLocalDevice();
            repository.AddOrUpdateLocalDevice(device);

            // 检查设备名是否更改
            if (existingDevice != null && existingDevice.DeviceName != device.DeviceName)
            {
                LocalDeviceNameChanged?.Invoke(this, device.DeviceName);
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "更新本地设备时出错");
        }
    }

    /// <summary>
    /// 初始化设备列表。
    ///
    /// <paramref name="localDeviceId"/> 为平台侧本机 uuid（仅用于排除自我记录）。
    /// 设备列表的<b>成员与运行时状态由 core 快照驱动</b>：此处只订阅快照并做一次同步刷新，
    /// 不再从平台库全量装载后自行维护在线状态。
    /// </summary>
    public async Task Initialize(string? localDeviceId = null)
    {
        this.localDeviceId = localDeviceId;

        snapshotStore.Refreshed -= OnSnapshotsRefreshed;
        snapshotStore.Refreshed += OnSnapshotsRefreshed;
        snapshotStore.Start();

        await Task.CompletedTask;
    }

    public string GeneratePairingCode()
    {
        return PairingCodeHelper.GenerateCode();
    }

    public string? GetCurrentPairingCode()
    {
        return PairingCodeHelper.GetCurrentCode();
    }

    public bool VerifyPairingCode(string code)
    {
        return PairingCodeHelper.VerifyCode(code);
    }
}
