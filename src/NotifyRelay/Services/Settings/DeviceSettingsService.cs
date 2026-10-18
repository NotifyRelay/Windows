using NotifyRelay.Data.AppDatabase.Repository;
using NotifyRelay.Data.Contracts;
using NotifyRelay.Data.Enums;

namespace NotifyRelay.Services.Settings;

internal sealed class DeviceSettingsService : IDeviceSettingsService
{
    private readonly SettingsRepository _settings;

    public string DeviceId { get; }

    public DeviceSettingsService(string deviceId, SettingsRepository settings)
    {
        DeviceId = deviceId;
        _settings = settings;
    }

    public bool ClipboardSyncEnabled
    {
        get => _settings.Get(DeviceId, nameof(ClipboardSyncEnabled), true);
        set => _settings.Set(DeviceId, nameof(ClipboardSyncEnabled), value);
    }

    public bool ImageToClipboardEnabled
    {
        get => _settings.Get(DeviceId, nameof(ImageToClipboardEnabled), false);
        set => _settings.Set(DeviceId, nameof(ImageToClipboardEnabled), value);
    }

    public bool ShowClipboardToast
    {
        get => _settings.Get(DeviceId, nameof(ShowClipboardToast), false);
        set => _settings.Set(DeviceId, nameof(ShowClipboardToast), value);
    }

    public bool OpenLinksInBrowser
    {
        get => _settings.Get(DeviceId, nameof(OpenLinksInBrowser), false);
        set => _settings.Set(DeviceId, nameof(OpenLinksInBrowser), value);
    }

    public bool NotificationSyncEnabled
    {
        get => _settings.Get(DeviceId, nameof(NotificationSyncEnabled), true);
        set => _settings.Set(DeviceId, nameof(NotificationSyncEnabled), value);
    }

    public bool ShowNotificationToast
    {
        get => _settings.Get(DeviceId, nameof(ShowNotificationToast), true);
        set => _settings.Set(DeviceId, nameof(ShowNotificationToast), value);
    }

    public bool ShowBadge
    {
        get => _settings.Get(DeviceId, nameof(ShowBadge), true);
        set => _settings.Set(DeviceId, nameof(ShowBadge), value);
    }

    public NotificationLaunchPreference NotificationLaunchPreference
    {
        get => _settings.Get(DeviceId, nameof(NotificationLaunchPreference), NotificationLaunchPreference.Dynamic);
        set => _settings.Set(DeviceId, nameof(NotificationLaunchPreference), (long)value);
    }

    public string ReceivedFilesPath
    {
        get => _settings.Get(DeviceId, nameof(ReceivedFilesPath), Constants.UserEnvironmentPaths.DownloadsPath);
        set => _settings.Set(DeviceId, nameof(ReceivedFilesPath), value);
    }

    public bool IgnoreWindowsApps
    {
        get => _settings.Get(DeviceId, nameof(IgnoreWindowsApps), true);
        set => _settings.Set(DeviceId, nameof(IgnoreWindowsApps), value);
    }

    public bool IgnoreNotificationDuringDnd
    {
        get => _settings.Get(DeviceId, nameof(IgnoreNotificationDuringDnd), true);
        set => _settings.Set(DeviceId, nameof(IgnoreNotificationDuringDnd), value);
    }

    public bool ClipboardFilesEnabled
    {
        get => _settings.Get(DeviceId, nameof(ClipboardFilesEnabled), false);
        set => _settings.Set(DeviceId, nameof(ClipboardFilesEnabled), value);
    }

    public string? ScrcpyPath
    {
        get => _settings.Get(DeviceId, nameof(ScrcpyPath), string.Empty);
        set => _settings.Set(DeviceId, nameof(ScrcpyPath), value);
    }

    public bool ScreenOff
    {
        get => _settings.Get(DeviceId, nameof(ScreenOff), true);
        set => _settings.Set(DeviceId, nameof(ScreenOff), value);
    }

    public bool PhysicalKeyboard
    {
        get => _settings.Get(DeviceId, nameof(PhysicalKeyboard), false);
        set => _settings.Set(DeviceId, nameof(PhysicalKeyboard), value);
    }

    public bool UnlockDeviceBeforeLaunch
    {
        get => _settings.Get(DeviceId, nameof(UnlockDeviceBeforeLaunch), false);
        set => _settings.Set(DeviceId, nameof(UnlockDeviceBeforeLaunch), value);
    }

    public int UnlockTimeout
    {
        get => _settings.Get(DeviceId, nameof(UnlockTimeout), 0);
        set => _settings.Set(DeviceId, nameof(UnlockTimeout), value);
    }

    public string? UnlockCommands
    {
        get => _settings.Get(DeviceId, nameof(UnlockCommands), string.Empty);
        set => _settings.Set(DeviceId, nameof(UnlockCommands), value);
    }

    public string? VideoBitrate
    {
        get => _settings.Get(DeviceId, nameof(VideoBitrate), "8M");
        set => _settings.Set(DeviceId, nameof(VideoBitrate), value);
    }

    public string? VideoResolution
    {
        get => _settings.Get(DeviceId, nameof(VideoResolution), string.Empty);
        set => _settings.Set(DeviceId, nameof(VideoResolution), value);
    }

    public string? VideoBuffer
    {
        get => _settings.Get(DeviceId, nameof(VideoBuffer), "0");
        set => _settings.Set(DeviceId, nameof(VideoBuffer), value);
    }

    public string? AudioBitrate
    {
        get => _settings.Get(DeviceId, nameof(AudioBitrate), "128K");
        set => _settings.Set(DeviceId, nameof(AudioBitrate), value);
    }

    public string? AudioBuffer
    {
        get => _settings.Get(DeviceId, nameof(AudioBuffer), string.Empty);
        set => _settings.Set(DeviceId, nameof(AudioBuffer), value);
    }

    public string? CustomArguments
    {
        get => _settings.Get(DeviceId, nameof(CustomArguments), string.Empty);
        set => _settings.Set(DeviceId, nameof(CustomArguments), value);
    }

    public bool DisableVideoForwarding
    {
        get => _settings.Get(DeviceId, nameof(DisableVideoForwarding), false);
        set => _settings.Set(DeviceId, nameof(DisableVideoForwarding), value);
    }

    public int VideoCodec
    {
        get => _settings.Get(DeviceId, nameof(VideoCodec), 0);
        set => _settings.Set(DeviceId, nameof(VideoCodec), value);
    }

    public string? FrameRate
    {
        get => _settings.Get(DeviceId, nameof(FrameRate), "60");
        set => _settings.Set(DeviceId, nameof(FrameRate), value);
    }

    public string? Crop
    {
        get => _settings.Get(DeviceId, nameof(Crop), string.Empty);
        set => _settings.Set(DeviceId, nameof(Crop), value);
    }

    public string? Display
    {
        get => _settings.Get(DeviceId, nameof(Display), "0");
        set => _settings.Set(DeviceId, nameof(Display), value);
    }

    public string? VirtualDisplaySize
    {
        get => _settings.Get(DeviceId, nameof(VirtualDisplaySize), string.Empty);
        set => _settings.Set(DeviceId, nameof(VirtualDisplaySize), value);
    }

    public int DisplayOrientation
    {
        get => _settings.Get(DeviceId, nameof(DisplayOrientation), 0);
        set => _settings.Set(DeviceId, nameof(DisplayOrientation), value);
    }

    public string? RotationAngle
    {
        get => _settings.Get(DeviceId, nameof(RotationAngle), "0");
        set => _settings.Set(DeviceId, nameof(RotationAngle), value);
    }

    public AudioOutputModeType AudioOutputMode
    {
        get => _settings.Get(DeviceId, nameof(AudioOutputMode), AudioOutputModeType.Desktop);
        set => _settings.Set(DeviceId, nameof(AudioOutputMode), value);
    }

    public bool ForwardMicrophone
    {
        get => _settings.Get(DeviceId, nameof(ForwardMicrophone), false);
        set => _settings.Set(DeviceId, nameof(ForwardMicrophone), value);
    }

    public string? AudioOutputBuffer
    {
        get => _settings.Get(DeviceId, nameof(AudioOutputBuffer), string.Empty);
        set => _settings.Set(DeviceId, nameof(AudioOutputBuffer), value);
    }

    public int AudioCodec
    {
        get => _settings.Get(DeviceId, nameof(AudioCodec), 0);
        set => _settings.Set(DeviceId, nameof(AudioCodec), value);
    }

    public string? AdbPath
    {
        get => _settings.Get(DeviceId, nameof(AdbPath), string.Empty);
        set => _settings.Set(DeviceId, nameof(AdbPath), value);
    }

    public bool AutoConnect
    {
        get => _settings.Get(DeviceId, nameof(AutoConnect), true);
        set => _settings.Set(DeviceId, nameof(AutoConnect), value);
    }

    public ScrcpyDevicePreferenceType ScrcpyDevicePreference
    {
        get => _settings.Get(DeviceId, nameof(ScrcpyDevicePreference), ScrcpyDevicePreferenceType.Auto);
        set => _settings.Set(DeviceId, nameof(ScrcpyDevicePreference), value);
    }

    public bool IsVirtualDisplayEnabled
    {
        get => _settings.Get(DeviceId, nameof(IsVirtualDisplayEnabled), true);
        set => _settings.Set(DeviceId, nameof(IsVirtualDisplayEnabled), value);
    }

    public bool MediaSessionSyncEnabled
    {
        get => _settings.Get(DeviceId, nameof(MediaSessionSyncEnabled), true);
        set => _settings.Set(DeviceId, nameof(MediaSessionSyncEnabled), value);
    }

    public bool AdbTcpipModeEnabled
    {
        get => _settings.Get(DeviceId, nameof(AdbTcpipModeEnabled), false);
        set => _settings.Set(DeviceId, nameof(AdbTcpipModeEnabled), value);
    }

    public bool AdbAutoConnect
    {
        get => _settings.Get(DeviceId, nameof(AdbAutoConnect), true);
        set => _settings.Set(DeviceId, nameof(AdbAutoConnect), value);
    }
}
