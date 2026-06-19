using System.Collections.Specialized;
using CommunityToolkit.WinUI;
using NotifyRelay.Data.AppDatabase.Models;
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
    public bool ConnectionStatus
    {
        get => connectionStatus;
        set
        {
            // 只有当连接状态真正改变时才执行操作
            if (connectionStatus == value)
            {
                // 移除连接状态未变化的调试日志
                return;
            }

            var wasConnected = connectionStatus;
            logger.LogInformation("设备 {0} ({1}) 在线状态变更：之前={2}, 现在={3}", Name, Id, wasConnected, value);

            if (value)
            {
                // 如果设置为true，取消任何挂起的断开连接操作
                disconnectDebounceTimer?.Stop();
                disconnectDebounceTimer?.Dispose();
                disconnectDebounceTimer = null;
                pendingDisconnect = false;

                SetProperty(ref connectionStatus, true);
                logger.LogInformation("设备 {0} ({1}) 已上线", Name, Id);

                // 如果设备之前未连接，并且已经发送过ftp请求，启动自动ftp请求计时器
                if (!wasConnected)
                {
                    logger.LogDebug("设备 {0} ({1}) 已连接，检查HasSentftpRequest属性", Name, Id);

                    // 只有当HasSentftpRequest为true时才启动计时器
                    if (HasSentftpRequest)
                    {
                        logger.LogDebug("HasSentftpRequest为true，启动自动ftp计时器");

                        // 确保之前的计时器已被释放
                        autoftpTimer?.Stop();
                        autoftpTimer?.Dispose();

                        // 启动5秒自动ftp请求计时器
                        autoftpTimer = new System.Timers.Timer(5000);
                        autoftpTimer.AutoReset = false; // 只触发一次
                        autoftpTimer.Elapsed += (s, e) =>
                        {
                            logger.LogDebug("设备 {0} ({1}) 的自动ftp计时器已触发", Name, Id);
                            App.MainWindow?.DispatcherQueue.TryEnqueue(() =>
                            {
                                try
                                {
                                    // 再次检查连接状态和HasSentftpRequest属性
                                    if (ConnectionStatus && HasSentftpRequest)
                                    {
                                        logger.LogDebug("设备仍然连接且HasSentftpRequest为true，发送ftp命令");

#if WINDOWS
                                        // 从DI获取networkDriveMapper并发送ftp命令
                                        var networkDriveMapper = Ioc.Default.GetRequiredService<NetworkDriveMapper>();
                                        networkDriveMapper.SendftpCommand(this, "start");
#endif
                                        logger.LogDebug("ftp命令发送成功");
                                    }
                                    else
                                    {
                                        logger.LogDebug("设备已断开连接或HasSentftpRequest为false，跳过发送ftp命令");
                                    }
                                }
                                catch (Exception ex)
                                {
                                    logger.LogError(ex, "设备 {0} ({1}) 的自动ftp请求失败", Name, Id);
                                }
                                finally
                                {
                                    // 确保计时器被释放
                                    autoftpTimer?.Dispose();
                                    autoftpTimer = null;
                                }
                            });
                        };
                        autoftpTimer.Start();
                        logger.LogDebug("设备 {0} ({1}) 的自动ftp计时器已启动", Name, Id);
                    }
                    else
                    {
                        logger.LogDebug("HasSentftpRequest为false，跳过启动自动ftp计时器");
                    }
                }
            }
            else if (connectionStatus && !pendingDisconnect)
            {
                // If setting to false and currently true, debounce
                pendingDisconnect = true;
                disconnectDebounceTimer?.Stop();
                disconnectDebounceTimer?.Dispose();
                disconnectDebounceTimer = new System.Timers.Timer(5000); // 5 second debounce
                var deviceName = Name;
                var deviceId = Id;
                disconnectDebounceTimer.Elapsed += (s, e) =>
                {
                    App.MainWindow?.DispatcherQueue.TryEnqueue(() =>
                    {
                        if (pendingDisconnect)
                        {
                            SetProperty(ref connectionStatus, false);
                            pendingDisconnect = false;
                            logger.LogInformation("设备 {0} ({1}) 已离线", deviceName, deviceId);
                        }
                        disconnectDebounceTimer?.Dispose();
                        disconnectDebounceTimer = null;
                    });
                };
                disconnectDebounceTimer.Start();
            }
            else if (!connectionStatus)
            {
                // Already false, do nothing
            }
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
    public string? RemoteBattery { get; set; }

    private System.Timers.Timer? disconnectDebounceTimer;
    private System.Timers.Timer? autoftpTimer;
    private bool pendingDisconnect;

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
                        var deviceEntity = new RemoteDeviceEntity
                        {
                            DeviceId = Id,
                            Name = Name,
                            Model = Model,
                            IpAddresses = IpAddresses ?? [],
                            SharedSecret = SharedSecret,
                            PublicKey = RemotePublicKey,
                            HasSentftpRequest = value
                        };
                        deviceRepository.AddOrUpdateRemoteDevice(deviceEntity);
                        logger.LogDebug("HasSentftpRequest属性已保存到数据库");
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
