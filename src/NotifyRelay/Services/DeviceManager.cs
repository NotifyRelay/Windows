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

public partial class DeviceManager(ILogger<DeviceManager> logger, DeviceRepository repository) : ObservableObject, IDeviceManager
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
    /// Finds a device session by device ID
    /// </summary>
    public PairedDevice? FindDeviceById(string deviceId)
    {
        return PairedDevices.FirstOrDefault(device => device.Id == deviceId);
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

    public void RemoveDevice(PairedDevice device)
    {
        logger.LogInformation("RemoveDevice: 开始移除设备 {deviceId} {deviceName}", device.Id, device.Name);
        NativeCore.RemoveDevice(device.Id);
        NativeCore.RemoveKnownDevice(device.Id);

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

    public void UpdateDeviceStatus(PairedDevice device, DeviceStatus deviceStatus)
    {
        var pairedDevice = PairedDevices.First(d => d.Id == device.Id);
        App.MainWindow.DispatcherQueue.EnqueueAsync(() =>
        {
            pairedDevice.Status = deviceStatus;
        });
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

    public async Task Initialize()
    {
        var pairedDevicesList = await repository.GetPairedDevices();

        // 清空现有集合，然后逐个添加设备，确保CollectionChanged事件被触发
        PairedDevices.Clear();
        foreach (var device in pairedDevicesList)
        {
            PairedDevices.Add(device);
        }

        ActiveDevice = PairedDevices.FirstOrDefault();
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
