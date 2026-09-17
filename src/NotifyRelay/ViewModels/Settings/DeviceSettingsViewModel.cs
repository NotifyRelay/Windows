using NotifyRelay.Data.AppDatabase.Repository;
using NotifyRelay.Data.Contracts;
using NotifyRelay.Data.Enums;
using NotifyRelay.Data.Items;
using NotifyRelay.Data.Models;
using NotifyRelay.Utils.Serialization;

namespace NotifyRelay.ViewModels.Settings;

public sealed partial class DeviceSettingsViewModel : BaseViewModel
{
    #region Display Properties
    public string DisplayIpAddresses
    {
        get
        {
            if (Device?.IpAddresses == null || Device.IpAddresses.Count == 0)
                return "No IP addresses";

            return string.Join(", ", Device.IpAddresses);
        }
    }
    #endregion

    #region Media Session Settings

    public bool MediaSessionSyncEnabled
    {
        get => DeviceSettings?.MediaSessionSyncEnabled ?? false;
        set
        {
            if (DeviceSettings != null && DeviceSettings.MediaSessionSyncEnabled != value)
            {
                DeviceSettings.MediaSessionSyncEnabled = value;
                OnPropertyChanged();
            }
        }
    }

    #endregion

    private readonly IAdbService AdbService = Ioc.Default.GetRequiredService<IAdbService>();
    private readonly IDeviceSettingsService DeviceSettings;
    public PairedDevice Device;

    private readonly RemoteAppRepository RemoteAppsRepository = Ioc.Default.GetRequiredService<RemoteAppRepository>();
    private readonly ISessionManager SessionManager = Ioc.Default.GetRequiredService<ISessionManager>();
    private readonly IftpService FtpService = Ioc.Default.GetRequiredService<IftpService>();
    private readonly IDeviceManager DeviceManager = Ioc.Default.GetRequiredService<IDeviceManager>();
    public ObservableCollection<ApplicationInfo> RemoteApps { get; set; } = [];


    public DeviceSettingsViewModel(PairedDevice device)
    {
        Device = device;
        DeviceSettings = device.DeviceSettings;
        OnPropertyChanged(nameof(DeviceSettings));

        selectedAudioOutputMode = AudioOutputModeOptions[AudioOutputMode];
        selectedScrcpyDevicePreference = ScrcpyDevicePreferenceOptions[ScrcpyDevicePreference];

        OnPropertyChanged(nameof(SelectedAudioOutputMode));
        OnPropertyChanged(nameof(SelectedScrcpyDevicePreference));
        LoadApps(device.Id);
    }

    public void LoadApps(string id)
    {
        RemoteApps = RemoteAppsRepository.GetApplicationsForDevice(id);
    }

    public void ChangeNotificationFilter(string notificationFilter, string appPackage)
    {
        var filterKey = ApplicationInfo.NotificationFilterTypes.First(f => f.Value == notificationFilter).Key;
        RemoteAppsRepository.UpdateAppNotificationFilter(Device!.Id, appPackage, filterKey);
        var app = RemoteApps.First(p => p.PackageName == appPackage);
        app.DeviceInfo.Filter = filterKey;
        app.SelectedNotificationFilter = notificationFilter;
    }

    [RelayCommand]
    public async Task RemoveDevice(PairedDevice? device)
    {
        if (device == null)
        {
            return;
        }

        var dialog = new ContentDialog
        {
            Title = "RemoveDeviceDialogTitle".GetLocalizedResource(),
            Content = string.Format("RemoveDeviceDialogSubtitle".GetLocalizedResource(), device.Name),
            PrimaryButtonText = "Remove".GetLocalizedResource(),
            CloseButtonText = "Cancel".GetLocalizedResource(),
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = App.MainWindow.Content!.XamlRoot
        };

        var result = await dialog.ShowAsync();

        if (result == ContentDialogResult.Primary)
        {
            try
            {
                if (device.ConnectionStatus)
                {
                    var message = new CommandMessage { CommandType = CommandType.Disconnect };
                    SessionManager.SendMessage(device.Id, SocketMessageSerializer.Serialize(message));
                    SessionManager.DisconnectDevice(device.Id);
                }

                FtpService.Remove(device.Id);
                if (!DeviceManager.RemoveDevice(device))
                {
                    var errorDialog = new ContentDialog
                    {
                        Title = "Error",
                        Content = "删除设备失败：Rust 持久化删除未完成，请重试",
                        CloseButtonText = "OK",
                        XamlRoot = App.MainWindow.Content!.XamlRoot
                    };
                    await errorDialog.ShowAsync();
                }
            }
            catch (Exception ex)
            {
                var errorDialog = new ContentDialog
                {
                    Title = "Error",
                    Content = $"删除设备失败：{ex.Message}",
                    CloseButtonText = "OK",
                    XamlRoot = App.MainWindow.Content!.XamlRoot
                };
                await errorDialog.ShowAsync();
            }
        }
    }
}
