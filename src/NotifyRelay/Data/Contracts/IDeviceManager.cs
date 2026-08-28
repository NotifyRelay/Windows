using NotifyRelay.Data.AppDatabase.Models;
using NotifyRelay.Data.Models;

namespace NotifyRelay.Data.Contracts;

public interface IDeviceManager
{
    /// <summary>
    /// Gets the list of connected clients.
    /// </summary>
    ObservableCollection<PairedDevice> PairedDevices { get; }

    /// <summary>
    /// Gets or sets the currently active device session
    /// </summary>
    PairedDevice? ActiveDevice { get; set; }

    /// <summary>
    /// Finds a device session by device ID
    /// </summary>
    PairedDevice? FindDeviceById(string deviceId);

    /// <summary>
    /// Updates an existing device in the collection or adds it if it doesn't exist.
    /// This method is thread-safe and handles UI thread dispatching internally.
    /// </summary>
    Task<PairedDevice> UpdateOrAddDeviceAsync(PairedDevice device, Action<PairedDevice>? updateAction = null);

    /// <summary>
    /// Gets the last connected device.
    /// </summary>
    Task<PairedDevice?> GetLastConnectedDevice();

    /// <summary>
    /// Removes the device from Rust 与数据库。
    /// Rust 持久化删除失败时返回 false 且不清理平台侧记录（防止重启后设备复活造成两端不一致）。
    /// </summary>
    bool RemoveDevice(PairedDevice device);

    /// <summary>
    /// 持久化设备变更（名称、IP等）到数据库
    /// </summary>
    void SaveDevice(PairedDevice device);

    /// <summary>
    /// Updates the device properties (battery..)
    /// </summary>
    void UpdateDeviceStatus(PairedDevice device, DeviceStatus deviceStatus);

    /// <summary>
    /// Returns the device if it get's successfully verified and added to the database.
    /// </summary>
    Task<PairedDevice?> VerifyHandshakeAsync(string deviceId, string remotePublicKey, string? deviceName, string? ipAddress);

    /// <summary>
    /// Gets the local device.
    /// </summary>
    Task<LocalDeviceEntity> GetLocalDeviceAsync();
    void UpdateLocalDevice(LocalDeviceEntity localDevice);
    Task Initialize();

    List<string> GetRemoteDeviceIpAddresses();

    /// <summary>
    /// Event fired when the local device name changes
    /// </summary>
    event EventHandler<string>? LocalDeviceNameChanged;

    /// <summary>
    /// 生成6位配对码，有效期5分钟
    /// </summary>
    string GeneratePairingCode();

    /// <summary>
    /// 获取当前有效的配对码，已过期返回null
    /// </summary>
    string? GetCurrentPairingCode();

    /// <summary>
    /// 验证配对码，验证成功后清除
    /// </summary>
    bool VerifyPairingCode(string code);
}
