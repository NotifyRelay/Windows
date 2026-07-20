using CommunityToolkit.WinUI;
using NotifyRelay.Data.AppDatabase.Models;
using NotifyRelay.Data.Contracts;
using NotifyRelay.Data.Enums;
using NotifyRelay.Extensions;
using NotifyRelay.Helpers;

namespace NotifyRelay.ViewModels.Settings;

public sealed partial class GeneralViewModel : BaseViewModel
{
    #region Services
    private readonly IUserSettingsService UserSettingsService = Ioc.Default.GetRequiredService<IUserSettingsService>();
    private readonly IDeviceManager _deviceManager = Ioc.Default.GetRequiredService<IDeviceManager>();
    private readonly IAdbService AdbService = Ioc.Default.GetRequiredService<IAdbService>();
    #endregion

    #region Properties
    // Theme settings
    public Theme CurrentTheme
    {
        get => UserSettingsService.GeneralSettingsService.Theme;
        set
        {
            if (value != UserSettingsService.GeneralSettingsService.Theme)
            {
                UserSettingsService.GeneralSettingsService.Theme = value;
                OnPropertyChanged();
            }
        }
    }

    public Dictionary<Theme, string> ThemeTypes { get; } = new()
    {
        { Theme.Default, "Default".GetLocalizedResource() },
        { Theme.Light, "ThemeLight/Content".GetLocalizedResource() },
        { Theme.Dark, "ThemeDark/Content".GetLocalizedResource() }
    };

    private string selectedThemeType;
    public string SelectedThemeType
    {
        get => selectedThemeType;
        set
        {
            if (SetProperty(ref selectedThemeType, value))
            {
                var newTheme = ThemeTypes.First(t => t.Value == value).Key;
                CurrentTheme = newTheme;
            }
        }
    }

    public StartupOptions StartupOption
    {
        get => UserSettingsService.GeneralSettingsService.StartupOption;
        set
        {
            if (value != UserSettingsService.GeneralSettingsService.StartupOption)
            {
                UserSettingsService.GeneralSettingsService.StartupOption = value;
                // Update startup task when option changes
                _ = AppLifecycleHelper.HandleStartupTaskAsync(value != StartupOptions.Disabled);
                OnPropertyChanged();
            }
        }
    }

    public Dictionary<StartupOptions, string> StartupTypes { get; } = new()
    {
        { StartupOptions.Disabled, "StartupOptionDisabled/Content".GetLocalizedResource() },
        { StartupOptions.InTray, "StartupOptionSystemTray/Content".GetLocalizedResource() },
        { StartupOptions.Minimized, "StartupOptionMinimized/Content".GetLocalizedResource() },
        { StartupOptions.Maximized, "StartupOptionMaximized/Content".GetLocalizedResource() }
    };

    private string selectedStartupType;
    public string SelectedStartupType
    {
        get => selectedStartupType;
        set
        {
            if (SetProperty(ref selectedStartupType, value))
            {
                StartupOption = StartupTypes.First(t => t.Value == value).Key;
            }
        }
    }

    private LocalDeviceEntity? localDevice;

    private string _localDeviceName = string.Empty;
    public string LocalDeviceName
    {
        get => _localDeviceName;
        set
        {
            if (SetProperty(ref _localDeviceName, value) && !string.IsNullOrWhiteSpace(value))
            {
                Task.Run(() =>
                {
                    if (localDevice != null)
                    {
                        localDevice.DeviceName = value;
                        _deviceManager.UpdateLocalDevice(localDevice);
                    }
                });
            }
        }
    }

    public string ScrcpyPath
    {
        get => UserSettingsService.GeneralSettingsService.ScrcpyPath;
        set
        {
            UserSettingsService.GeneralSettingsService.ScrcpyPath = value;
            OnPropertyChanged();
        }
    }

    public string AdbPath
    {
        get => UserSettingsService.GeneralSettingsService.AdbPath;
        set
        {
            UserSettingsService.GeneralSettingsService.AdbPath = value;
            OnPropertyChanged();
            AdbService.StartAsync();
        }
    }

    public MediaMessageReceiveMode MediaMessageReceiveMode
    {
        get => UserSettingsService.GeneralSettingsService.MediaMessageReceiveMode;
        set
        {
            if (value != UserSettingsService.GeneralSettingsService.MediaMessageReceiveMode)
            {
                UserSettingsService.GeneralSettingsService.MediaMessageReceiveMode = value;
                OnPropertyChanged();
            }
        }
    }

    public Dictionary<MediaMessageReceiveMode, string> MediaMessageReceiveModes { get; } = new()
    {
        { MediaMessageReceiveMode.On, "ReceiveMediaMessagesOn".GetLocalizedResource() },
        { MediaMessageReceiveMode.Off, "ReceiveMediaMessagesOff".GetLocalizedResource() },
        { MediaMessageReceiveMode.AudioOnly, "ReceiveMediaMessagesAudioOnly".GetLocalizedResource() }
    };

    private string selectedMediaMessageReceiveMode;
    public string SelectedMediaMessageReceiveMode
    {
        get => selectedMediaMessageReceiveMode;
        set
        {
            if (SetProperty(ref selectedMediaMessageReceiveMode, value))
            {
                MediaMessageReceiveMode = MediaMessageReceiveModes.First(t => t.Value == value).Key;
            }
        }
    }

    public string ReceivedFilesPath
    {
        get => UserSettingsService.GeneralSettingsService.ReceivedFilesPath;
        set
        {
            if (value != UserSettingsService.GeneralSettingsService.ReceivedFilesPath)
            {
                UserSettingsService.GeneralSettingsService.ReceivedFilesPath = value;
                OnPropertyChanged();
            }
        }
    }

    public bool EnableSendMediaNotifications
    {
        get => UserSettingsService.GeneralSettingsService.EnableSendMediaNotifications;
        set
        {
            if (value != UserSettingsService.GeneralSettingsService.EnableSendMediaNotifications)
            {
                UserSettingsService.GeneralSettingsService.EnableSendMediaNotifications = value;
                OnPropertyChanged();
            }
        }
    }

    public string RemoteStoragePath
    {
        get => UserSettingsService.GeneralSettingsService.RemoteStoragePath;
        set
        {
            // TODO : Delete the previous remote storage folder or move all the placeholders to the new location
            if (value != UserSettingsService.GeneralSettingsService.RemoteStoragePath)
            {
                UserSettingsService.GeneralSettingsService.RemoteStoragePath = value;
                var ftpService = Ioc.Default.GetRequiredService<IftpService>();
                //ftpService.RemoveAllSyncRoots();
                OnPropertyChanged();
            }
        }
    }
    #endregion

    public GeneralViewModel()
    {
        selectedThemeType = ThemeTypes[CurrentTheme];
        selectedStartupType = StartupTypes[StartupOption];
        selectedMediaMessageReceiveMode = MediaMessageReceiveModes[MediaMessageReceiveMode];

        // Load initial local device name
        LoadLocalDeviceName();
    }

    private void LoadLocalDeviceName()
    {
        _ = dispatcher.EnqueueAsync(async () =>
        {
            localDevice = await _deviceManager.GetLocalDeviceAsync();
            LocalDeviceName = localDevice.DeviceName;
        });
    }
}
