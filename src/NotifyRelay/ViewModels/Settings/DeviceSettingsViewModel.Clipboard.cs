namespace NotifyRelay.ViewModels.Settings;

public sealed partial class DeviceSettingsViewModel
{
    #region Clipboard Settings
    public bool ClipboardSyncEnabled
    {
        get => DeviceSettings?.ClipboardSyncEnabled ?? true;
        set
        {
            if (DeviceSettings != null && DeviceSettings.ClipboardSyncEnabled != value)
            {
                DeviceSettings.ClipboardSyncEnabled = value;
                OnPropertyChanged();
            }
        }
    }

    public bool OpenLinksInBrowser
    {
        get => DeviceSettings?.OpenLinksInBrowser ?? false;
        set
        {
            if (DeviceSettings != null && DeviceSettings.OpenLinksInBrowser != value)
            {
                DeviceSettings.OpenLinksInBrowser = value;
                OnPropertyChanged();
            }
        }
    }

    public bool ShowClipboardToast
    {
        get => DeviceSettings?.ShowClipboardToast ?? false;
        set
        {
            if (DeviceSettings != null && DeviceSettings.ShowClipboardToast != value)
            {
                DeviceSettings.ShowClipboardToast = value;
                OnPropertyChanged();
            }
        }
    }

    public bool ClipboardFilesEnabled
    {
        get => DeviceSettings?.ClipboardFilesEnabled ?? false;
        set
        {
            if (DeviceSettings != null && DeviceSettings.ClipboardFilesEnabled != value)
            {
                DeviceSettings.ClipboardFilesEnabled = value;
                OnPropertyChanged();
            }
        }
    }

    public bool ImageToClipboardEnabled
    {
        get => DeviceSettings?.ImageToClipboardEnabled ?? false;
        set
        {
            if (DeviceSettings != null && DeviceSettings.ImageToClipboardEnabled != value)
            {
                DeviceSettings.ImageToClipboardEnabled = value;
                OnPropertyChanged();
            }
        }
    }
    #endregion
}
