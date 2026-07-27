using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.Advertisement;
using Windows.Devices.Bluetooth.GenericAttributeProfile;
using Windows.Storage.Streams;

namespace NotifyRelay.Services.HeartRate;

/// <summary>心率 BLE 连接状态。</summary>
public enum HeartRateConnectionState
{
    Disconnected,
    Scanning,
    Connecting,
    Connected
}

/// <summary>扫描发现的心率设备信息。</summary>
public sealed class HeartRateDeviceInfo
{
    public ulong Address { get; init; }
    public string Name { get; init; } = string.Empty;
    public override string ToString() => Name;
}

/// <summary>
/// BLE 心率服务：扫描（仅广播标准心率服务 0x180D 的设备）、手动连接、
/// 订阅心率测量特征值 0x2A37 并按标准格式解析，断开与状态事件。
/// 不记忆设备，每次手动扫描连接。
/// </summary>
public sealed class HeartRateBleService : IDisposable
{
    private static readonly Guid HeartRateServiceUuid = GattServiceUuids.HeartRate;                 // 0000180D-...
    private static readonly Guid HeartRateMeasurementUuid = GattCharacteristicUuids.HeartRateMeasurement; // 00002A37-...

    private readonly ILogger<HeartRateBleService> _logger;
    private readonly object _sync = new();

    private BluetoothLEAdvertisementWatcher? _watcher;
    private BluetoothLEDevice? _device;
    private GattDeviceService? _service;
    private GattCharacteristic? _characteristic;

    private volatile HeartRateConnectionState _state = HeartRateConnectionState.Disconnected;

    /// <summary>收到心率值（BLE 回调线程触发）。</summary>
    public event Action<int>? HeartRateReceived;

    /// <summary>连接状态变化（任意线程触发）。</summary>
    public event Action<HeartRateConnectionState>? StateChanged;

    /// <summary>扫描期间发现新设备（任意线程触发）。</summary>
    public event Action<HeartRateDeviceInfo>? DeviceDiscovered;

    public HeartRateConnectionState State => _state;

    public HeartRateBleService(ILogger<HeartRateBleService> logger)
    {
        _logger = logger;
    }

    private void SetState(HeartRateConnectionState state)
    {
        if (_state == state) return;
        _state = state;
        _logger.LogInformation("心率 BLE 状态变更: {State}", state);
        StateChanged?.Invoke(state);
    }

    /// <summary>开始扫描广播心率服务的 BLE 设备；重复调用会先停止上一次扫描。</summary>
    public void StartScan()
    {
        lock (_sync)
        {
            StopScanInternal();

            var watcher = new BluetoothLEAdvertisementWatcher
            {
                ScanningMode = BluetoothLEScanningMode.Active
            };
            // 仅列出广播标准心率服务 0x180D 的设备
            watcher.AdvertisementFilter.Advertisement.ServiceUuids.Add(HeartRateServiceUuid);

            var seen = new HashSet<ulong>();
            watcher.Received += (_, args) =>
            {
                string name = args.Advertisement.LocalName;
                if (string.IsNullOrWhiteSpace(name)) name = $"未知设备 ({args.BluetoothAddress:X12})";
                lock (seen)
                {
                    if (!seen.Add(args.BluetoothAddress)) return;
                }
                DeviceDiscovered?.Invoke(new HeartRateDeviceInfo { Address = args.BluetoothAddress, Name = name });
            };

            _watcher = watcher;
            watcher.Start();
            SetState(HeartRateConnectionState.Scanning);
        }
    }

    /// <summary>停止扫描（不影响已建立的连接）。</summary>
    public void StopScan()
    {
        lock (_sync)
        {
            StopScanInternal();
            if (_state == HeartRateConnectionState.Scanning)
                SetState(_characteristic != null ? HeartRateConnectionState.Connected : HeartRateConnectionState.Disconnected);
        }
    }

    private void StopScanInternal()
    {
        if (_watcher != null)
        {
            try { _watcher.Stop(); } catch { /* 忽略停止异常 */ }
            _watcher = null;
        }
    }

    /// <summary>连接指定地址的设备并订阅心率通知。</summary>
    public async Task<bool> ConnectAsync(ulong address)
    {
        Disconnect();
        lock (_sync) StopScanInternal();
        SetState(HeartRateConnectionState.Connecting);

        try
        {
            var device = await BluetoothLEDevice.FromBluetoothAddressAsync(address);
            if (device == null)
            {
                _logger.LogWarning("心率 BLE 连接失败：无法获取设备对象 {Address:X12}", address);
                SetState(HeartRateConnectionState.Disconnected);
                return false;
            }

            var servicesResult = await device.GetGattServicesForUuidAsync(HeartRateServiceUuid, BluetoothCacheMode.Uncached);
            if (servicesResult.Status != GattCommunicationStatus.Success || servicesResult.Services.Count == 0)
            {
                _logger.LogWarning("心率 BLE 连接失败：未找到心率服务，状态 {Status}", servicesResult.Status);
                device.Dispose();
                SetState(HeartRateConnectionState.Disconnected);
                return false;
            }
            var service = servicesResult.Services[0];

            var charResult = await service.GetCharacteristicsForUuidAsync(HeartRateMeasurementUuid, BluetoothCacheMode.Uncached);
            if (charResult.Status != GattCommunicationStatus.Success || charResult.Characteristics.Count == 0)
            {
                _logger.LogWarning("心率 BLE 连接失败：未找到心率测量特征值，状态 {Status}", charResult.Status);
                service.Dispose();
                device.Dispose();
                SetState(HeartRateConnectionState.Disconnected);
                return false;
            }
            var characteristic = charResult.Characteristics[0];

            var status = await characteristic.WriteClientCharacteristicConfigurationDescriptorAsync(
                GattClientCharacteristicConfigurationDescriptorValue.Notify);
            if (status != GattCommunicationStatus.Success)
            {
                _logger.LogWarning("心率 BLE 连接失败：订阅通知失败，状态 {Status}", status);
                service.Dispose();
                device.Dispose();
                SetState(HeartRateConnectionState.Disconnected);
                return false;
            }

            characteristic.ValueChanged += OnHeartRateValueChanged;
            device.ConnectionStatusChanged += OnConnectionStatusChanged;

            lock (_sync)
            {
                _device = device;
                _service = service;
                _characteristic = characteristic;
            }
            SetState(HeartRateConnectionState.Connected);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "心率 BLE 连接异常 {Address:X12}", address);
            Disconnect();
            return false;
        }
    }

    private void OnConnectionStatusChanged(BluetoothLEDevice sender, object args)
    {
        if (sender.ConnectionStatus == BluetoothConnectionStatus.Disconnected)
        {
            _logger.LogInformation("心率 BLE 设备已断开连接");
            Disconnect();
        }
    }

    private void OnHeartRateValueChanged(GattCharacteristic sender, GattValueChangedEventArgs args)
    {
        try
        {
            var reader = DataReader.FromBuffer(args.CharacteristicValue);
            byte[] data = new byte[reader.UnconsumedBufferLength];
            reader.ReadBytes(data);
            if (data.Length < 2) return;

            // 标准心率测量格式：flags bit0 = 1 时为 uint16 (little-endian)，否则为 uint8
            int bpm = (data[0] & 0x01) != 0 && data.Length >= 3
                ? data[1] | (data[2] << 8)
                : data[1];
            if (bpm > 0)
                HeartRateReceived?.Invoke(bpm);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "心率数据解析失败");
        }
    }

    /// <summary>断开当前连接并释放资源。</summary>
    public void Disconnect()
    {
        BluetoothLEDevice? device;
        GattDeviceService? service;
        GattCharacteristic? characteristic;
        lock (_sync)
        {
            device = _device;
            service = _service;
            characteristic = _characteristic;
            _device = null;
            _service = null;
            _characteristic = null;
        }

        if (characteristic != null)
        {
            characteristic.ValueChanged -= OnHeartRateValueChanged;
            try
            {
                _ = characteristic.WriteClientCharacteristicConfigurationDescriptorAsync(
                    GattClientCharacteristicConfigurationDescriptorValue.None);
            }
            catch { /* 忽略取消订阅异常 */ }
        }
        if (device != null)
        {
            device.ConnectionStatusChanged -= OnConnectionStatusChanged;
        }
        service?.Dispose();
        device?.Dispose();

        if (device != null || characteristic != null || _state != HeartRateConnectionState.Scanning)
            SetState(HeartRateConnectionState.Disconnected);
    }

    public void Dispose()
    {
        lock (_sync) StopScanInternal();
        Disconnect();
    }
}
