using NotifyRelay.Data.AppDatabase.Repository;
using NotifyRelay.Data.Contracts;
using NotifyRelay.Data.Enums;
using NotifyRelay.Data.Models;
using NotifyRelay.Utils;
using NotifyRelay.Utils.Serialization;

namespace NotifyRelay.ViewModels;
public sealed partial class MainPageViewModel : BaseViewModel
{
    #region Services
    private IDeviceManager DeviceManager { get; } = Ioc.Default.GetRequiredService<IDeviceManager>();
    private IScreenMirrorService ScreenMirrorService { get; } = Ioc.Default.GetRequiredService<IScreenMirrorService>();
    public INotificationService NotificationService { get; } = Ioc.Default.GetRequiredService<INotificationService>();
    private RemoteAppRepository RemoteAppsRepository { get; } = Ioc.Default.GetRequiredService<RemoteAppRepository>();
    private ISessionManager SessionManager { get; } = Ioc.Default.GetRequiredService<ISessionManager>();
    private IUpdateService UpdateService { get; } = Ioc.Default.GetRequiredService<IUpdateService>();
    private IFileTransferService FileTransferService { get; } = Ioc.Default.GetRequiredService<IFileTransferService>();
    private IMessageHandler MessageHandler { get; } = Ioc.Default.GetRequiredService<IMessageHandler>();
    private IPlaybackService PlaybackService { get; } = Ioc.Default.GetRequiredService<IPlaybackService>();
    #endregion

    #region Properties
    public ObservableCollection<PairedDevice> PairedDevices => DeviceManager.PairedDevices;
    public ReadOnlyObservableCollection<Notification> Notifications => NotificationService.NotificationHistory;
    public ReadOnlyObservableCollection<GroupedNotification> GroupedNotifications => NotificationService.GroupedNotificationHistory;

    // 合并后的仪表盘项目集合（包含媒体块和通知）
    public ObservableCollection<object> DashboardItems { get; } = new ObservableCollection<object>();

    // 混合集合，包含所有通知（分组和单个）
    public ObservableCollection<object> MixedNotifications
    {
        get
        {
            var mixed = new ObservableCollection<object>();

            // 获取所有分组通知
            var grouped = GroupedNotifications.ToList();

            // 获取所有分组使用的应用包名
            var groupedPackageNames = new HashSet<string>(grouped.Select(g => g.AppPackage ?? "UnknownApp"));

            // 添加所有分组通知
            foreach (var group in grouped)
            {
                mixed.Add(group);
            }

            // 添加未分组的单个通知
            foreach (var notification in Notifications)
            {
                string packageName = notification.AppPackage ?? "UnknownApp";
                if (!groupedPackageNames.Contains(packageName))
                {
                    mixed.Add(notification);
                }
            }

            return mixed;
        }
    }
    public PairedDevice? Device => DeviceManager.ActiveDevice;

    /// <summary>
    /// 当前显示的音乐媒体块列表（支持多个设备同时显示）
    /// </summary>
    public ReadOnlyObservableCollection<MusicMediaBlock> CurrentMusicMediaBlocks => NotificationService.CurrentMusicMediaBlocks;

    [ObservableProperty]
    public partial bool LoadingScrcpy { get; set; } = false;

    public bool IsUpdateAvailable => UpdateService.IsUpdateAvailable;

    /// <summary>
    /// 当前设备是否正在运行仅音频模式的 scrcpy
    /// </summary>
    public bool IsAudioOnlyRunning
    {
        get
        {
            if (Device == null)
                return false;
            return ScreenMirrorService.IsAudioOnlyRunning(Device.Id);
        }
    }

    /// <summary>
    /// 获取当前音频状态的图标
    /// </summary>
    public string AudioStatusIcon
    {
        get
        {
            return IsAudioOnlyRunning ? "\uE995" : "\uE74F";
        }
    }

    /// <summary>
    /// 获取当前音频状态的文本描述
    /// </summary>
    public string AudioStatusText
    {
        get
        {
            return IsAudioOnlyRunning ? "仅音频播放中" : "未转发音频";
        }
    }

    /// <summary>
    /// 获取所有连接的ADB设备类型
    /// </summary>
    public List<string> AdbConnectionTypes
    {
        get
        {
            var connectionTypes = new List<string>();

            if (Device == null || !Device.HasAdbConnection || Device.ConnectedAdbDevices.Count == 0)
            {
                return connectionTypes;
            }

            // 检查所有连接的ADB设备，添加所有连接类型
            if (Device.ConnectedAdbDevices.Any(d => d.Type == NotifyRelay.Data.Enums.DeviceType.USB))
            {
                connectionTypes.Add("USB");
            }

            if (Device.ConnectedAdbDevices.Any(d => d.Type == NotifyRelay.Data.Enums.DeviceType.WIFI))
            {
                connectionTypes.Add("WiFi");
            }

            return connectionTypes;
        }
    }

    /// <summary>
    /// 获取ADB设备的详细信息，用于悬浮提示
    /// </summary>
    public string AdbDeviceInfo
    {
        get
        {
            var deviceInfo = new List<string>();

            // 设备名称
            if (!string.IsNullOrEmpty(Device?.Name))
            {
                deviceInfo.Add(Device.Name);
            }

            // 设备型号
            if (!string.IsNullOrEmpty(Device?.Model))
            {
                deviceInfo.Add(Device.Model);
            }

            // IP地址
            if (Device?.IpAddresses != null && Device.IpAddresses.Count > 0)
            {
                deviceInfo.Add(string.Join(", ", Device.IpAddresses));
            }

            // 确保至少返回一个默认值，便于调试
            if (deviceInfo.Count == 0)
            {
                deviceInfo.Add("设备信息不可用");
                if (Device == null)
                {
                    deviceInfo.Add("Device为null");
                }
                else if (Device.ConnectedAdbDevices.Count == 0)
                {
                    deviceInfo.Add("ConnectedAdbDevices为空");
                }
            }

            return string.Join("\n", deviceInfo);
        }
    }

    /// <summary>
    /// 获取所有连接的ADB设备图标
    /// </summary>
    public List<string> AdbStatusIcons
    {
        get
        {
            var icons = new List<string>();

            if (Device == null || !Device.HasAdbConnection || Device.ConnectedAdbDevices.Count == 0)
            {
                return icons;
            }

            // 添加USB图标
            if (Device.ConnectedAdbDevices.Any(d => d.Type == NotifyRelay.Data.Enums.DeviceType.USB))
            {
                icons.Add("\uE89E"); // USB图标
            }

            // 添加WiFi图标
            if (Device.ConnectedAdbDevices.Any(d => d.Type == NotifyRelay.Data.Enums.DeviceType.WIFI))
            {
                icons.Add("\uE927"); // WiFi图标
            }

            return icons;
        }
    }
    #endregion

    public MainPageViewModel()
    {
        // 用于存储之前的设备，以便移除事件监听
        PairedDevice? previousDevice = null;

        // 当 DeviceManager.ActiveDevice 变化时，让 x:Bind 的 Device 属性重新求值
        if (DeviceManager is INotifyPropertyChanged npc)
        {
            npc.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName == nameof(IDeviceManager.ActiveDevice))
                {
                    // 移除之前设备的事件监听
                    if (previousDevice is INotifyPropertyChanged prevNpc)
                    {
                        prevNpc.PropertyChanged -= OnDevicePropertyChanged;
                    }
                    if (previousDevice != null)
                    {
                        previousDevice.ConnectedAdbDevices.CollectionChanged -= OnAdbDevicesCollectionChanged;
                    }

                    OnPropertyChanged(nameof(Device));
                    OnPropertyChanged(nameof(IsAudioOnlyRunning));
                    OnPropertyChanged(nameof(AudioStatusIcon));
                    OnPropertyChanged(nameof(AudioStatusText));
                    OnPropertyChanged(nameof(AdbConnectionTypes));
                    OnPropertyChanged(nameof(AdbStatusIcons));
                    OnPropertyChanged(nameof(AdbDeviceInfo));

                    // 添加新设备的事件监听
                    previousDevice = Device;
                    if (previousDevice is INotifyPropertyChanged newNpc)
                    {
                        newNpc.PropertyChanged += OnDevicePropertyChanged;
                    }
                    if (previousDevice != null)
                    {
                        previousDevice.ConnectedAdbDevices.CollectionChanged += OnAdbDevicesCollectionChanged;
                    }
                }
            };
        }

        // 监听 NotificationService 的 PropertyChanged 事件，当 MediaBlocks 列表变化时触发 UI 更新（集合自身变更由集合通知）
        if (NotificationService is INotifyPropertyChanged npc2)
        {
            npc2.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName == nameof(INotificationService.CurrentMusicMediaBlocks))
                {
                    OnPropertyChanged(nameof(CurrentMusicMediaBlocks));
                    // 当媒体块集合实例改变时，更新 DashboardItems
                    UpdateDashboardItems();
                }
            };
        }

        // 初始化 DashboardItems 并监听集合变化
        InitializeDashboardItems();
    }

    private void InitializeDashboardItems()
    {
        // 初始填充
        UpdateDashboardItems();

        // 监听 GroupedNotifications 的变化
        if (GroupedNotifications is System.Collections.Specialized.INotifyCollectionChanged groupedNcc)
        {
            groupedNcc.CollectionChanged += (s, e) => UpdateDashboardItems();
        }

        // 注意：CurrentMusicMediaBlocks 是 ReadOnlyObservableCollection，我们需要监听其内部集合的变化
        // 这里简化处理：如果在 NotificationService 中 CurrentMusicMediaBlocks 的实例被替换，我们在上面的 PropertyChanged 中处理
        // 如果只是内容变化，我们也需要监听。
        // 由于 NotificationService.CurrentMusicMediaBlocks 可能已经在上面被监听了 PropertyChanged，
        // 这里我们尝试监听 CollectionChanged。
        if (CurrentMusicMediaBlocks is System.Collections.Specialized.INotifyCollectionChanged mediaNcc)
        {
            mediaNcc.CollectionChanged += (s, e) => UpdateDashboardItems();
        }
    }

    /// <summary>
    /// 更新 DashboardItems 集合
    /// 为了保证性能，这里尽量做增量更新，但为了实现简单和稳健，先采用智能重置策略
    /// </summary>
    private void UpdateDashboardItems()
    {
        // 如果是在非 UI 线程调用，可能需要 Dispatcher，但通常 ViewModel 的 PropertyChanged 会由 UI 框架处理
        // 这里假设是在 UI 线程或框架能处理 ObservableCollection 的跨线程操作（WinUI 3 通常需要 DispatcherQueue，但这里先直接操作）

        // 简单策略：清空并重新添加。为了减少闪烁，可以比较差异。
        // 但 ItemsRepeater 处理 Clear + Add 可能会导致滚动位置丢失。
        // 优化策略：
        // 1. 确保 MediaBlocks 在最前
        // 2. 确保 Notifications 在后

        // 由于 MediaBlocks 很少变动，Notifications 变动频繁，我们分别处理。

        // 现在的简单实现：完全重建。
        // TODO: 后续优化为增量更新以保持滚动位置和性能

        // 实际上，为了避免 ItemsRepeater 闪烁，我们应该尽量复用现有的集合

        var newItems = new List<object>();
        if (CurrentMusicMediaBlocks != null)
        {
            newItems.AddRange(CurrentMusicMediaBlocks);
        }
        if (GroupedNotifications != null)
        {
            newItems.AddRange(GroupedNotifications);
        }

        // 简单的 Diff 算法：如果数量差距不大，且大部分元素相同

        // 如果 DashboardItems 为空，直接添加
        if (DashboardItems.Count == 0)
        {
            foreach (var item in newItems)
            {
                DashboardItems.Add(item);
            }
            return;
        }

        // 粗暴的同步方法：
        // 1. 移除多余的
        // 2. 添加新增的
        // 3. 移动顺序不对的（这里暂不处理顺序移动，假设顺序相对稳定）

        // 为了简单起见，我们使用一个临时列表来同步
        // 注意：这种同步在大量数据下可能效率不高，但在通知列表场景下（通常几十条）是可以接受的

        DashboardItems.Clear();
        foreach (var item in newItems)
        {
            DashboardItems.Add(item);
        }
    }

    /// <summary>
    /// 手动刷新音频状态属性，用于UI更新
    /// </summary>
    public void RefreshAudioStatus()
    {
        OnPropertyChanged(nameof(IsAudioOnlyRunning));
        OnPropertyChanged(nameof(AudioStatusIcon));
        OnPropertyChanged(nameof(AudioStatusText));
    }

    /// <summary>
    /// 设备属性变化时的事件处理方法
    /// </summary>
    private void OnDevicePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(PairedDevice.HasAdbConnection) ||
            e.PropertyName == nameof(PairedDevice.Name) ||
            e.PropertyName == nameof(PairedDevice.Model) ||
            e.PropertyName == nameof(PairedDevice.IpAddresses))
        {
            OnPropertyChanged(nameof(AdbConnectionTypes));
            OnPropertyChanged(nameof(AdbStatusIcons));
            OnPropertyChanged(nameof(AdbDeviceInfo));
        }
    }

    /// <summary>
    /// ADB设备集合变化时的事件处理方法
    /// </summary>
    private void OnAdbDevicesCollectionChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
    {
        OnPropertyChanged(nameof(AdbConnectionTypes));
        OnPropertyChanged(nameof(AdbStatusIcons));
        OnPropertyChanged(nameof(AdbDeviceInfo));
    }

    /// <summary>
    /// 根据设备ID获取设备名称
    /// </summary>
    public string GetDeviceName(string deviceId)
    {
        var device = PairedDevices.FirstOrDefault(d => d.Id == deviceId);
        return device?.Name ?? deviceId;
    }

    #region Commands

    [RelayCommand]
    public async Task ToggleConnection(PairedDevice? device)
    {
        if (Device!.ConnectionStatus)
        {
            var message = new CommandMessage { CommandType = CommandType.Disconnect };
            SessionManager.SendMessage(Device.Id, SocketMessageSerializer.Serialize(message));
            await Task.Delay(50);
            SessionManager.DisconnectDevice(Device.Id);
            Device.ConnectionStatus = false;
        }
    }

    [RelayCommand]
    public async Task StartScrcpy()
    {
        try
        {
            LoadingScrcpy = true;
            await ScreenMirrorService.StartScrcpy(Device!);
        }
        finally
        {
            await Task.Delay(1000);
            LoadingScrcpy = false;
        }
    }

    [RelayCommand]
    public void SwitchToNextDevice(int delta)
    {
        if (PairedDevices.Count <= 1)
            return;

        var currentIndex = -1;
        for (int i = 0; i < PairedDevices.Count; i++)
        {
            if (PairedDevices[i].Id == Device?.Id)
            {
                currentIndex = i;
                break;
            }
        }

        if (currentIndex == -1)
            return;

        int nextIndex;
        if (delta < 0)
        {
            // Move to next device (or loop back to first)
            nextIndex = (currentIndex + 1) % PairedDevices.Count;
        }
        else
        {
            // Move to previous device (or loop to last)
            nextIndex = (currentIndex - 1 + PairedDevices.Count) % PairedDevices.Count;
        }

        DeviceManager.ActiveDevice = PairedDevices[nextIndex];
    }

    [RelayCommand]
    public async void SetRingerMode(string? modeStr)
    {
        if (int.TryParse(modeStr, out int mode))
        {
            // 模式2：仅音频播放中，模式0：未转发音频
            if (mode == 2)
            {
                // 启动仅音频模式的 scrcpy
                await ScreenMirrorService.StartScrcpy(Device!, "--no-video");
            }
            else if (mode == 0)
            {
                // 停止 scrcpy 进程
                ScreenMirrorService.StopScrcpyByDeviceId(Device!.Id);
            }
            // 不再发送铃声模式消息到设备

            // 刷新音频状态属性，更新UI
            RefreshAudioStatus();
        }
    }

    [RelayCommand]
    public void ClearAllNotificationall()
    {
        NotificationService.ClearAllNotificationall();
    }

    [RelayCommand]
    public void Update()
    {
        UpdateService.DownloadUpdatesAsync();
    }

    [RelayCommand]
    public void RemoveNotification(Notification notification)
    {
        NotificationService.RemoveNotification(Device!, notification);
    }

    [RelayCommand]
    public void ClearAllNotifications(string appPackage)
    {
        NotificationService.ClearAllNotifications(appPackage);
    }

    [RelayCommand]
    public void SendMediaControl(string mediaControlParam)
    {
        if (string.IsNullOrEmpty(mediaControlParam))
        {
            return;
        }

        // 解析参数：格式为 "deviceId:action"
        var parts = mediaControlParam.Split(':');
        if (parts.Length != 2)
        {
            return;
        }

        string deviceId = parts[0];
        string action = parts[1];

        // 发送媒体控制请求到指定设备
        PlaybackService.SendMediaControlRequest(deviceId, action);
    }

    [RelayCommand]
    public void StartftpConnection()
    {
        if (Device != null)
        {
            // 设置手动发送过ftp请求的标记
            Device.HasSentftpRequest = true;
            MessageHandler.SendftpCommand(Device, "start");
        }
    }

    #endregion

    #region Methods

    public async Task OpenApp(Notification notification, string? deviceId = null)
    {
        Debug.WriteLine($"[调试] MainPageViewModel.OpenApp 被调用：notification.Key={notification?.Key} deviceId={deviceId}");

        // 如果未指定设备ID，使用当前活跃设备
        var targetDevice = deviceId != null ? DeviceManager.FindDeviceById(deviceId) : Device;
        if (targetDevice == null)
        {
            Debug.WriteLine("[警告] 找不到目标设备（targetDevice 为 null），取消打开应用。请检查 deviceId 是否正确或设备是否已配对。");
            return;
        }

        var notificationToInvoke = new NotificationMessage
        {
            NotificationType = NotificationType.Invoke,
            NotificationKey = notification.Key,
        };
        string? appIcon = string.Empty;
        if (!string.IsNullOrEmpty(notification.AppPackage))
        {
            appIcon = IconUtils.GetAppIconFilePath(notification.AppPackage);
        }

        Debug.WriteLine($"[调试] 调用 ScreenMirrorService.StartScrcpy: deviceId={targetDevice.Id} appPackage={notification.AppPackage} appIcon={appIcon}");
        var started = await ScreenMirrorService.StartScrcpy(targetDevice, $"--new-display --start-app={notification.AppPackage}", appIcon);

        Debug.WriteLine($"[调试] ScreenMirrorService.StartScrcpy 返回: started={started}");

        // Scrcpy doesn't have a way of opening notifications afaik, so we will just have the notification listener on Android to open it for us
        // Plus we have to wait (2s will do ig?) until the app is actually launched to send the intent for launching the notification since Google added a lot more restrictions in this particular case
        if (started && targetDevice.ConnectionStatus)
        {
            Debug.WriteLine($"[调试] scrcpy 已启动且设备连接，等待 2s 然后发送通知调用到设备 {targetDevice.Id}");
            await Task.Delay(2000);
            SessionManager.SendMessage(targetDevice.Id, SocketMessageSerializer.Serialize(notificationToInvoke));
        }
    }

    public void UpdateNotificationFilter(string appPackage)
    {
        RemoteAppsRepository.UpdateAppNotificationFilter(Device!.Id, appPackage, NotificationFilter.Disabled);
    }

    public void ToggleNotificationPin(Notification notification)
    {
        if (Device != null)
        {
            NotificationService.TogglePinNotification(Device, notification);
        }
    }

    public void SendFiles(IReadOnlyList<IStorageItem> storageItems)
    {
        FileTransferService.SendFiles(storageItems);
    }

    #endregion

}
