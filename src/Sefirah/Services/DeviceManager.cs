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
                existingDevice.SharedSecret = device.SharedSecret;
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

    public Task<RemoteDeviceEntity> GetDeviceInfoAsync(string deviceId)
    {
        throw new NotImplementedException();
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
        NativeCore.RemoveDevice(device.Id);

        App.MainWindow.DispatcherQueue.EnqueueAsync(() =>
        {
            try
            {
                var existing = PairedDevices.FirstOrDefault(d => d.Id == device.Id);
                if (existing is null) return;

                PairedDevices.Remove(existing);
                repository.DeletePairedDevice(existing.Id);

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

    public Task UpdateDevice(RemoteDeviceEntity device)
    {
        throw new NotImplementedException();
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
            var keyB64 = NativeCore.ExportDeviceKey(deviceId);
            if (keyB64 == null)
            {
                logger.LogError("导出设备密钥失败: {deviceId}", deviceId);
                return null;
            }
            var sharedSecretBytes = Convert.FromBase64String(keyB64);

            if (repository.HasDevice(deviceId, out var existingDevice))
            {
                existingDevice.LastConnected = DateTime.Now;
                existingDevice.Name = deviceName ?? existingDevice.Name;
                existingDevice.PublicKey = remotePublicKey;
                existingDevice.SharedSecret = sharedSecretBytes;

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
                var deviceId = Guid.NewGuid().ToString();
                var stateJson = NativeCore.ExportState();
                var encryptedState = stateJson != null ? NativeCore.EncryptLocalState(stateJson, deviceId) : null;
                localDevice = new LocalDeviceEntity
                {
                    DeviceId = deviceId,
                    DeviceName = name,
                    PublicKey = Encoding.UTF8.GetBytes(publicKeyBase64 ?? string.Empty),
                    StateJson = encryptedState ?? string.Empty,
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
                        var newState = NativeCore.ExportState();
                        localDevice.StateJson = newState != null ? NativeCore.EncryptLocalState(newState, localDevice.DeviceId) ?? string.Empty : string.Empty;
                        repository.AddOrUpdateLocalDevice(localDevice);
                    }
                }
                else
                {
                    var cachedPubKey = Encoding.UTF8.GetString(localDevice.PublicKey ?? []);
                    if (rustPubKey != cachedPubKey)
                    {
                        localDevice.PublicKey = Encoding.UTF8.GetBytes(rustPubKey);
                        var updatedState = NativeCore.ExportState();
                        localDevice.StateJson = updatedState != null ? NativeCore.EncryptLocalState(updatedState, localDevice.DeviceId) ?? string.Empty : string.Empty;
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
