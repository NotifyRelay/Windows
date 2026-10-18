namespace NotifyRelay.ViewModels.Settings;

public sealed partial class DeviceSettingsViewModel
{
    #region Notification Settings
    public bool NotificationSyncEnabled
    {
        get => DeviceSettings?.NotificationSyncEnabled ?? true;
        set
        {
            if (DeviceSettings != null && DeviceSettings.NotificationSyncEnabled != value)
            {
                DeviceSettings.NotificationSyncEnabled = value;
                OnPropertyChanged();
            }
        }
    }

    public bool ShowNotificationToast
    {
        get => DeviceSettings?.ShowNotificationToast ?? false;
        set
        {
            if (DeviceSettings != null && DeviceSettings.ShowNotificationToast != value)
            {
                DeviceSettings.ShowNotificationToast = value;
                OnPropertyChanged();
            }
        }
    }

    public bool ShowBadge
    {
        get => DeviceSettings?.ShowBadge ?? true;
        set
        {
            if (DeviceSettings != null && DeviceSettings.ShowBadge != value)
            {
                DeviceSettings.ShowBadge = value;
                OnPropertyChanged();
            }
        }
    }

    public bool IgnoreWindowsApps
    {
        get => DeviceSettings?.IgnoreWindowsApps ?? false;
        set
        {
            if (DeviceSettings != null && DeviceSettings.IgnoreWindowsApps != value)
            {
                DeviceSettings.IgnoreWindowsApps = value;
                OnPropertyChanged();
            }
        }
    }

    public bool IgnoreNotificationDuringDnd
    {
        get => DeviceSettings?.IgnoreNotificationDuringDnd ?? false;
        set
        {
            if (DeviceSettings != null && DeviceSettings.IgnoreNotificationDuringDnd != value)
            {
                DeviceSettings.IgnoreNotificationDuringDnd = value;
                OnPropertyChanged();
            }
        }
    }
    #endregion
}
