using System.Collections.Specialized;
using CommunityToolkit.WinUI;
using NotifyRelay.Data.AppDatabase.Repository;
using NotifyRelay.Data.Contracts;
using NotifyRelay.Services.Socket;

#if WINDOWS
using NotifyRelay.Platforms.Windows.Services;
#endif

namespace NotifyRelay.Data.Models;

public partial class PairedDevice : ObservableObject
{
    public string Id { get; private set; }

    private string name = string.Empty;
    public string Name
    {
        get => name;
        set => SetProperty(ref name, value);
    }

    public string Model { get; set; } = string.Empty;

    public List<string>? IpAddresses { get; set; } = [];

    private ImageSource? wallpaper;
    public ImageSource? Wallpaper
    {
        get => wallpaper;
        set => SetProperty(ref wallpaper, value);
    }

    private bool connectionStatus;

    /// <summary>
    /// 是否在线。**在线判定归 Rust core**（快照 online：已配对 12s / 未配对 20s 内收到心跳即在线），
    /// 平台端不再自行判决离线时机（原 5 秒防抖已移除），唯一写入方为
    /// <see cref="NotifyRelay.Services.DeviceManager"/> 应用 core 快照时。
    /// </summary>
    public bool ConnectionStatus
    {
        get => connectionStatus;
        set
        {
            if (connectionStatus == value) return;

            var wasConnected = connectionStatus;
            SetProperty(ref connectionStatus, value);

            if (value)
            {
                logger.LogInformation("设备 {0} ({1}) 已上线", Name, Id);
                if (!wasConnected) TryStartAutoFtp();
            }
            else
            {
                logger.LogInformation("设备 {0} ({1}) 已离线", Name, Id);
            }
        }
    }

    /// <summary>
    /// 设备由离线转为在线后，若此前手动发起过 ftp 请求，则延迟重连映射盘。
    /// （平台端特有的映射盘能力，触发时机由 core 快照的在线状态驱动）
    /// </summary>
    private void TryStartAutoFtp()
    {
        if (!HasSentftpRequest) return;

        // 确保之前的计时器已被释放
        autoftpTimer?.Stop();
        autoftpTimer?.Dispose();

        autoftpTimer = new System.Timers.Timer(5000) { AutoReset = false };
        autoftpTimer.Elapsed += (s, e) =>
        {
            App.MainWindow?.DispatcherQueue.TryEnqueue(() =>
            {
                try
                {
#if WINDOWS
                    if (ConnectionStatus && HasSentftpRequest)
                    {
                        Ioc.Default.GetRequiredService<NetworkDriveMapper>().SendftpCommand(this, "start");
                    }
#endif
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "设备 {0} ({1}) 的自动ftp请求失败", Name, Id);
                }
                finally
                {
                    autoftpTimer?.Dispose();
                    autoftpTimer = null;
                }
            });
        };
        autoftpTimer.Start();
    }

    /// <summary>
    /// 用 Rust core 快照回填运行时状态（在线/电量/名称/IP/最后可见时间）。
    ///
    /// 这些字段的唯一真源是 core：平台端只做投影，不再各自维护第二份。
    /// 快照中的空值（core 重启首帧的 name/deviceType）不覆盖平台侧已有值；
    /// 名称额外走 <see cref="DeviceNameCache"/> 兜底，保证离线设备不显示为 uuid。
    /// </summary>
    public void ApplySnapshot(DeviceSnapshot snapshot)
    {
        ConnectionStatus = snapshot.Online;

        var battery = snapshot.BatteryPercent;
        if (battery >= 0)
        {
            Status = new DeviceStatus
            {
                BatteryStatus = battery,
                ChargingStatus = snapshot.IsCharging,
            };
        }

        // 名称：core 快照优先，其次平台侧已有名，最后 uuid→名称缓存兜底
        var resolvedName = !string.IsNullOrWhiteSpace(snapshot.Name)
            ? snapshot.Name
            : (!string.IsNullOrWhiteSpace(Name) ? Name : DeviceNameCache.TryGetDisplayName(Id));
        if (!string.IsNullOrWhiteSpace(resolvedName))
        {
            Name = resolvedName;
            DeviceNameCache.Update(Id, resolvedName);
        }

        if (!string.IsNullOrWhiteSpace(snapshot.Ip))
        {
            RemoteIpAddress = snapshot.Ip;
        }

        if (!string.IsNullOrWhiteSpace(snapshot.DeviceType)
            && snapshot.DeviceType != DeviceSnapshot.UnknownDeviceType)
        {
            RemoteDeviceType = snapshot.DeviceType;
        }

        if (snapshot.LastSeen > 0)
        {
            LastHeartbeat = snapshot.LastSeenTime.UtcDateTime;
        }
    }

    private ServerSession? session;
    public ServerSession? Session
    {
        get => session;
        set => SetProperty(ref session, value);
    }

    private DeviceStatus? status;
    public DeviceStatus? Status
    {
        get => status;
        set => SetProperty(ref status, value);
    }

    // Notify 协议会话所需信息
    public byte[]? SharedSecret { get; set; }
    public string? RemotePublicKey { get; set; }
    public DateTime? LastHeartbeat { get; set; }
    public string? RemoteIpAddress { get; set; }
    public string? RemoteDeviceType { get; set; }

    private System.Timers.Timer? autoftpTimer;

    private readonly IAdbService adbService;
    private readonly IUserSettingsService userSettingsService;
    private readonly ILogger<PairedDevice> logger;

    private IDeviceSettingsService deviceSettings;
    public IDeviceSettingsService DeviceSettings
    {
        get => deviceSettings;
        private set => SetProperty(ref deviceSettings, value);
    }

    private bool hasSentftpRequest;
    public bool HasSentftpRequest
    {
        get => hasSentftpRequest;
        set
        {
            if (SetProperty(ref hasSentftpRequest, value))
            {
                logger.LogDebug("HasSentftpRequest属性值已更新为：{value}", value);
                // 当属性变化时保存到数据库
                App.MainWindow?.DispatcherQueue.TryEnqueue(() =>
                {
                    try
                    {
                        var deviceRepository = Ioc.Default.GetRequiredService<DeviceRepository>();
                        if (deviceRepository.HasDevice(Id, out var deviceEntity))
                        {
                            deviceEntity.HasSentftpRequest = value;
                            deviceRepository.AddOrUpdateRemoteDevice(deviceEntity);
                            logger.LogDebug("HasSentftpRequest属性已保存到数据库");
                        }
                    }
                    catch (Exception ex)
                    {
                        logger.LogError(ex, "保存HasSentftpRequest属性到数据库失败");
                    }
                });
            }
        }
    }


    public PairedDevice(string Id)
    {
        this.Id = Id;
        userSettingsService = Ioc.Default.GetRequiredService<IUserSettingsService>();
        adbService = Ioc.Default.GetRequiredService<IAdbService>();
        logger = Ioc.Default.GetRequiredService<ILogger<PairedDevice>>();
        adbService.AdbDevices.CollectionChanged += OnAdbDevicesChanged;
        deviceSettings = userSettingsService.GetDeviceSettings(Id);
    }

    public ObservableCollection<AdbDevice> ConnectedAdbDevices { get; set; } = [];

    public bool HasAdbConnection
    {
        get
        {
            try
            {
                if (adbService == null)
                {
                    return false;
                }

                var pairedDeviceId = Id;
                var pairedDeviceModel = Model;

                foreach (var adbDevice in adbService.AdbDevices)
                {
                    var isOnline = adbDevice.IsOnline;
                    var androidIdMatch = !string.IsNullOrEmpty(adbDevice.AndroidId) && adbDevice.AndroidId == pairedDeviceId;
                    var modelMatch = string.IsNullOrEmpty(adbDevice.AndroidId) &&
                                     !string.IsNullOrEmpty(adbDevice.Model) &&
                                     !string.IsNullOrEmpty(pairedDeviceModel) &&
                                     (pairedDeviceModel.Equals(adbDevice.Model, StringComparison.OrdinalIgnoreCase) ||
                                      pairedDeviceModel.Contains(adbDevice.Model, StringComparison.OrdinalIgnoreCase) ||
                                      adbDevice.Model.Contains(pairedDeviceModel, StringComparison.OrdinalIgnoreCase));

                    if (isOnline && (androidIdMatch || modelMatch))
                    {
                        return true;
                    }
                }

                return false;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "检查 ADB 连接时出错");
                return false;
            }
        }
    }

    private void OnAdbDevicesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        RefreshConnectedAdbDevices();
        OnPropertyChanged(nameof(HasAdbConnection));
    }

    private async void RefreshConnectedAdbDevices()
    {
        try
        {
            // Use UI thread if available
            await App.MainWindow.DispatcherQueue.EnqueueAsync(() =>
            {
                ConnectedAdbDevices.Clear();

                var devices = adbService.AdbDevices
                    .Where(adbDevice => adbDevice.IsOnline &&
                        (
                            (!string.IsNullOrEmpty(adbDevice.AndroidId) && adbDevice.AndroidId == Id) ||
                            (string.IsNullOrEmpty(adbDevice.AndroidId) &&
                                !string.IsNullOrEmpty(adbDevice.Model) &&
                                !string.IsNullOrEmpty(Model) &&
                                (Model.Equals(adbDevice.Model, StringComparison.OrdinalIgnoreCase) ||
                                Model.Contains(adbDevice.Model, StringComparison.OrdinalIgnoreCase) ||
                                adbDevice.Model.Contains(Model, StringComparison.OrdinalIgnoreCase)))
                        ))
                    .ToList();

                ConnectedAdbDevices.AddRange(devices);
            });
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Error in RefreshConnectedAdbDevices: {ex.Message}");
        }
    }
}
