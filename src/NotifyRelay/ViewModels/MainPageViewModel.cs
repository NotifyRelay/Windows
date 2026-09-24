using NotifyRelay.Data.AppDatabase.Repository;
using NotifyRelay.Data.Contracts;
using NotifyRelay.Data.Enums;
using NotifyRelay.Data.Models;
using NotifyRelay.Utils;
using NotifyRelay.Utils.Serialization;
using NotifyRelay.Platforms.Windows.Services;

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
    private NetworkDriveMapper NetworkDriveMapper { get; } = Ioc.Default.GetRequiredService<NetworkDriveMapper>();
    private IPlaybackService PlaybackService { get; } = Ioc.Default.GetRequiredService<IPlaybackService>();
    #endregion

    #region Properties
    public ObservableCollection<PairedDevice> PairedDevices => DeviceManager.PairedDevices;
    public ReadOnlyObservableCollection<Notification> Notifications => NotificationService.NotificationHistory;
    public ReadOnlyObservableCollection<GroupedNotification> GroupedNotifications => NotificationService.GroupedNotificationHistory;

    public PairedDevice? Device => DeviceManager.ActiveDevice;

    /// <summary>
    /// 当前显示的音乐媒体块列表（支持多个设备同时显示）
    /// </summary>
    public ReadOnlyObservableCollection<MusicMediaBlock> CurrentMusicMediaBlocks => NotificationService.CurrentMusicMediaBlocks;

    [ObservableProperty]
    public partial bool LoadingScrcpy { get; set; } = false;

    public bool IsUpdateAvailable => UpdateService.IsUpdateAvailable;
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
    /// 根据设备ID获取设备名称
    /// </summary>
    public string GetDeviceName(string deviceId)
    {
        var device = PairedDevices.FirstOrDefault(d => d.Id == deviceId);
        return device?.Name ?? deviceId;
    }
}
