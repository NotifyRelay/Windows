using NotifyRelay.Data.Configuration;
using NotifyRelay.Data.Contracts;
using NotifyRelay.Data.Enums;

namespace NotifyRelay.Services.Settings;

internal sealed class DeviceSettingsService : IDeviceSettingsService
{
    private readonly IConfigurationRoot _configuration;

    public string DeviceId { get; }

    public DeviceSettingsService(string deviceId, IConfigurationRoot configuration)
    {
        DeviceId = deviceId;
        _configuration = configuration;
    }

    private string SettingsKey(string settingName) => SqliteConfigurationProvider.BuildKey(DeviceId, settingName);

    public bool ClipboardSyncEnabled
    {
        get => _configuration.Get(SettingsKey(nameof(ClipboardSyncEnabled)), true);
        set => _configuration.Set(SettingsKey(nameof(ClipboardSyncEnabled)), value);
    }

    public bool ImageToClipboardEnabled
    {
        get => _configuration.Get(SettingsKey(nameof(ImageToClipboardEnabled)), false);
        set => _configuration.Set(SettingsKey(nameof(ImageToClipboardEnabled)), value);
    }

    public bool ShowClipboardToast
    {
        get => _configuration.Get(SettingsKey(nameof(ShowClipboardToast)), false);
        set => _configuration.Set(SettingsKey(nameof(ShowClipboardToast)), value);
    }

    public bool OpenLinksInBrowser
    {
        get => _configuration.Get(SettingsKey(nameof(OpenLinksInBrowser)), false);
        set => _configuration.Set(SettingsKey(nameof(OpenLinksInBrowser)), value);
    }

    public bool NotificationSyncEnabled
    {
        get => _configuration.Get(SettingsKey(nameof(NotificationSyncEnabled)), true);
        set => _configuration.Set(SettingsKey(nameof(NotificationSyncEnabled)), value);
    }

    public bool ShowNotificationToast
    {
        get => _configuration.Get(SettingsKey(nameof(ShowNotificationToast)), true);
        set => _configuration.Set(SettingsKey(nameof(ShowNotificationToast)), value);
    }

    public bool ShowBadge
    {
        get => _configuration.Get(SettingsKey(nameof(ShowBadge)), true);
        set => _configuration.Set(SettingsKey(nameof(ShowBadge)), value);
    }

    public NotificationLaunchPreference NotificationLaunchPreference
    {
        get => _configuration.Get(SettingsKey(nameof(NotificationLaunchPreference)), NotificationLaunchPreference.Dynamic);
        set => _configuration.Set(SettingsKey(nameof(NotificationLaunchPreference)), (long)value);
    }

    public string RemoteStoragePath
    {
        get => _configuration.Get(SettingsKey(nameof(RemoteStoragePath)), Constants.UserEnvironmentPaths.DefaultRemoteDevicePath)!;
        set => _configuration.Set(SettingsKey(nameof(RemoteStoragePath)), value);
    }

    public string ReceivedFilesPath
    {
        get => _configuration.Get(SettingsKey(nameof(ReceivedFilesPath)), Constants.UserEnvironmentPaths.DownloadsPath)!;
        set => _configuration.Set(SettingsKey(nameof(ReceivedFilesPath)), value);
    }

    public bool IgnoreWindowsApps
    {
        get => _configuration.Get(SettingsKey(nameof(IgnoreWindowsApps)), true);
        set => _configuration.Set(SettingsKey(nameof(IgnoreWindowsApps)), value);
    }

    public bool IgnoreNotificationDuringDnd
    {
        get => _configuration.Get(SettingsKey(nameof(IgnoreNotificationDuringDnd)), true);
        set => _configuration.Set(SettingsKey(nameof(IgnoreNotificationDuringDnd)), value);
    }

    public bool ClipboardFilesEnabled
    {
        get => _configuration.Get(SettingsKey(nameof(ClipboardFilesEnabled)), false);
        set => _configuration.Set(SettingsKey(nameof(ClipboardFilesEnabled)), value);
    }

    public string? ScrcpyPath
    {
        get => _configuration.Get(SettingsKey(nameof(ScrcpyPath)), string.Empty);
        set => _configuration.Set(SettingsKey(nameof(ScrcpyPath)), value);
    }

    public bool ScreenOff
    {
        get => _configuration.Get(SettingsKey(nameof(ScreenOff)), true);
        set => _configuration.Set(SettingsKey(nameof(ScreenOff)), value);
    }

    public bool PhysicalKeyboard
    {
        get => _configuration.Get(SettingsKey(nameof(PhysicalKeyboard)), false);
        set => _configuration.Set(SettingsKey(nameof(PhysicalKeyboard)), value);
    }

    public bool UnlockDeviceBeforeLaunch
    {
        get => _configuration.Get(SettingsKey(nameof(UnlockDeviceBeforeLaunch)), false);
        set => _configuration.Set(SettingsKey(nameof(UnlockDeviceBeforeLaunch)), value);
    }

    public int UnlockTimeout
    {
        get => _configuration.Get(SettingsKey(nameof(UnlockTimeout)), 0);
        set => _configuration.Set(SettingsKey(nameof(UnlockTimeout)), value);
    }

    public string? UnlockCommands
    {
        get => _configuration.Get(SettingsKey(nameof(UnlockCommands)), string.Empty);
        set => _configuration.Set(SettingsKey(nameof(UnlockCommands)), value);
    }

    public string? VideoBitrate
    {
        get => _configuration.Get(SettingsKey(nameof(VideoBitrate)), "8M");
        set => _configuration.Set(SettingsKey(nameof(VideoBitrate)), value);
    }

    public string? VideoResolution
    {
        get => _configuration.Get(SettingsKey(nameof(VideoResolution)), string.Empty);
        set => _configuration.Set(SettingsKey(nameof(VideoResolution)), value);
    }

    public string? VideoBuffer
    {
        get => _configuration.Get(SettingsKey(nameof(VideoBuffer)), "0");
        set => _configuration.Set(SettingsKey(nameof(VideoBuffer)), value);
    }

    public string? AudioBitrate
    {
        get => _configuration.Get(SettingsKey(nameof(AudioBitrate)), "128K");
        set => _configuration.Set(SettingsKey(nameof(AudioBitrate)), value);
    }

    public string? AudioBuffer
    {
        get => _configuration.Get(SettingsKey(nameof(AudioBuffer)), string.Empty);
        set => _configuration.Set(SettingsKey(nameof(AudioBuffer)), value);
    }

    public string? CustomArguments
    {
        get => _configuration.Get(SettingsKey(nameof(CustomArguments)), string.Empty);
        set => _configuration.Set(SettingsKey(nameof(CustomArguments)), value);
    }

    public bool DisableVideoForwarding
    {
        get => _configuration.Get(SettingsKey(nameof(DisableVideoForwarding)), false);
        set => _configuration.Set(SettingsKey(nameof(DisableVideoForwarding)), value);
    }

    public int VideoCodec
    {
        get => _configuration.Get(SettingsKey(nameof(VideoCodec)), 0);
        set => _configuration.Set(SettingsKey(nameof(VideoCodec)), value);
    }

    public string? FrameRate
    {
        get => _configuration.Get(SettingsKey(nameof(FrameRate)), "60");
        set => _configuration.Set(SettingsKey(nameof(FrameRate)), value);
    }

    public string? Crop
    {
        get => _configuration.Get(SettingsKey(nameof(Crop)), string.Empty);
        set => _configuration.Set(SettingsKey(nameof(Crop)), value);
    }

    public string? Display
    {
        get => _configuration.Get(SettingsKey(nameof(Display)), "0");
        set => _configuration.Set(SettingsKey(nameof(Display)), value);
    }

    public string? VirtualDisplaySize
    {
        get => _configuration.Get(SettingsKey(nameof(VirtualDisplaySize)), string.Empty);
        set => _configuration.Set(SettingsKey(nameof(VirtualDisplaySize)), value);
    }

    public int DisplayOrientation
    {
        get => _configuration.Get(SettingsKey(nameof(DisplayOrientation)), 0);
        set => _configuration.Set(SettingsKey(nameof(DisplayOrientation)), value);
    }

    public string? RotationAngle
    {
        get => _configuration.Get(SettingsKey(nameof(RotationAngle)), "0");
        set => _configuration.Set(SettingsKey(nameof(RotationAngle)), value);
    }

    public AudioOutputModeType AudioOutputMode
    {
        get => _configuration.Get(SettingsKey(nameof(AudioOutputMode)), AudioOutputModeType.Desktop);
        set => _configuration.Set(SettingsKey(nameof(AudioOutputMode)), value);
    }

    public bool ForwardMicrophone
    {
        get => _configuration.Get(SettingsKey(nameof(ForwardMicrophone)), false);
        set => _configuration.Set(SettingsKey(nameof(ForwardMicrophone)), value);
    }

    public string? AudioOutputBuffer
    {
        get => _configuration.Get(SettingsKey(nameof(AudioOutputBuffer)), string.Empty);
        set => _configuration.Set(SettingsKey(nameof(AudioOutputBuffer)), value);
    }

    public int AudioCodec
    {
        get => _configuration.Get(SettingsKey(nameof(AudioCodec)), 0);
        set => _configuration.Set(SettingsKey(nameof(AudioCodec)), value);
    }

    public string? AdbPath
    {
        get => _configuration.Get(SettingsKey(nameof(AdbPath)), string.Empty);
        set => _configuration.Set(SettingsKey(nameof(AdbPath)), value);
    }

    public bool AutoConnect
    {
        get => _configuration.Get(SettingsKey(nameof(AutoConnect)), true);
        set => _configuration.Set(SettingsKey(nameof(AutoConnect)), value);
    }

    public ScrcpyDevicePreferenceType ScrcpyDevicePreference
    {
        get => _configuration.Get(SettingsKey(nameof(ScrcpyDevicePreference)), ScrcpyDevicePreferenceType.Auto);
        set => _configuration.Set(SettingsKey(nameof(ScrcpyDevicePreference)), value);
    }

    public bool IsVirtualDisplayEnabled
    {
        get => _configuration.Get(SettingsKey(nameof(IsVirtualDisplayEnabled)), true);
        set => _configuration.Set(SettingsKey(nameof(IsVirtualDisplayEnabled)), value);
    }

    public bool MediaSessionSyncEnabled
    {
        get => _configuration.Get(SettingsKey(nameof(MediaSessionSyncEnabled)), true);
        set => _configuration.Set(SettingsKey(nameof(MediaSessionSyncEnabled)), value);
    }

    public bool AdbTcpipModeEnabled
    {
        get => _configuration.Get(SettingsKey(nameof(AdbTcpipModeEnabled)), false);
        set => _configuration.Set(SettingsKey(nameof(AdbTcpipModeEnabled)), value);
    }

    public bool AdbAutoConnect
    {
        get => _configuration.Get(SettingsKey(nameof(AdbAutoConnect)), true);
        set => _configuration.Set(SettingsKey(nameof(AdbAutoConnect)), value);
    }
}
