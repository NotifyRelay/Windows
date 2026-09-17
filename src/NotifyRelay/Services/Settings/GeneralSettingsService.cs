using CommunityToolkit.WinUI;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using NotifyRelay.Data.Configuration;
using NotifyRelay.Data.Contracts;
using NotifyRelay.Data.Enums;
using NotifyRelay.Data.Models.Actions;
using NotifyRelay.Platforms.Windows.Services;
using NotifyRelay.Services.Overlay;
using Windows.UI;
using Windows.UI.ViewManagement;

namespace NotifyRelay.Services.Settings;

internal sealed partial class GeneralSettingsService : IGeneralSettingsService, IOverlaySettings
{
    private readonly IConfigurationRoot _configuration;
    private readonly UISettings _uiSettings = new();
    private bool _isApplyingTheme;

    public GeneralSettingsService(IConfigurationRoot configuration)
    {
        _configuration = configuration;

        // Listen for system theme changes
        _uiSettings.ColorValuesChanged += (s, e) =>
        {
            if (Theme == Theme.Default)
            {
                _ = App.MainWindow?.DispatcherQueue.EnqueueAsync(() =>
                {
                    ApplyTheme(App.MainWindow, null, Theme.Default);
                });
            }
        };

        // Initialize theme
        ApplyTheme(App.MainWindow, null, Theme);
    }

    private string SettingsKey(string settingName) => SqliteConfigurationProvider.BuildKey(null, settingName);

    public StartupOptions StartupOption
    {
        get => _configuration.Get(SettingsKey(nameof(StartupOption)), StartupOptions.InTray);
        set => _configuration.Set(SettingsKey(nameof(StartupOption)), value);
    }

    public Theme Theme
    {
        get => _configuration.Get(SettingsKey(nameof(Theme)), Theme.Default);
        set
        {
            if (_configuration.Set(SettingsKey(nameof(Theme)), value))
            {
                ApplyTheme(App.MainWindow, null, value);
            }
        }
    }

    public void ApplyTheme(Window? window = null, AppWindowTitleBar? titleBar = null, Theme? theme = null)
    {
        if (_isApplyingTheme) return;

        try
        {
            _isApplyingTheme = true;

            window ??= App.MainWindow;
            if (window?.Content == null) return;

            titleBar ??= window.AppWindow?.TitleBar;
            theme ??= Theme;

            // Update root element theme
            if (window.Content is FrameworkElement rootElement)
            {
                rootElement.RequestedTheme = theme switch
                {
                    Theme.Light => ElementTheme.Light,
                    Theme.Dark => ElementTheme.Dark,
                    _ => ElementTheme.Default
                };
            }
#if WINDOWS
            // Update titlebar
            if (titleBar is not null)
            {
                titleBar.ButtonBackgroundColor = Colors.Transparent;
                titleBar.ButtonInactiveBackgroundColor = Colors.Transparent;

                switch (theme)
                {
                    case Theme.Default:
                        titleBar.ButtonHoverBackgroundColor = (Color)Application.Current.Resources["SystemBaseLowColor"];
                        titleBar.ButtonForegroundColor = (Color)Application.Current.Resources["SystemBaseHighColor"];
                        break;
                    case Theme.Light:
                        titleBar.ButtonHoverBackgroundColor = Color.FromArgb(51, 0, 0, 0);
                        titleBar.ButtonForegroundColor = Colors.Black;
                        break;
                    case Theme.Dark:
                        titleBar.ButtonHoverBackgroundColor = Color.FromArgb(51, 255, 255, 255);
                        titleBar.ButtonForegroundColor = Colors.White;
                        break;
                }
            }
#endif
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Error applying theme: {ex}");
        }
        finally
        {
            _isApplyingTheme = false;
        }
    }

    public string ReceivedFilesPath
    {
        get => _configuration.Get(SettingsKey(nameof(ReceivedFilesPath)), Constants.UserEnvironmentPaths.DownloadsPath)!;
        set => _configuration.Set(SettingsKey(nameof(ReceivedFilesPath)), value);
    }

    public string ScrcpyPath
    {
        get => _configuration.Get(SettingsKey(nameof(ScrcpyPath)), string.Empty)!;
        set => _configuration.Set(SettingsKey(nameof(ScrcpyPath)), value);
    }

    public string AdbPath
    {
        get => _configuration.Get(SettingsKey(nameof(AdbPath)), string.Empty)!;
        set => _configuration.Set(SettingsKey(nameof(AdbPath)), value);
    }

    public MediaMessageReceiveMode MediaMessageReceiveMode
    {
        get => _configuration.Get(SettingsKey(nameof(MediaMessageReceiveMode)), MediaMessageReceiveMode.AudioOnly);
        set => _configuration.Set(SettingsKey(nameof(MediaMessageReceiveMode)), value);
    }

    public List<BaseAction> Actions
    {
        get => _configuration.Get(SettingsKey(nameof(Actions)), new List<BaseAction>())!;
        set => _configuration.Set(SettingsKey(nameof(Actions)), value);
    }

    public void AddAction(BaseAction action)
    {
        var actions = Actions.ToList();
        actions.Add(action);
        Actions = actions;
    }

    public void UpdateAction(BaseAction action)
    {
        var actions = Actions.ToList();
        var index = actions.FindIndex(a => a.Id == action.Id);
        if (index != -1)
        {
            actions.RemoveAt(index);
            actions.Insert(index, action);
            Actions = actions;
        }
    }

    public void RemoveAction(BaseAction action)
    {
        var actions = Actions.ToList();
        var index = actions.FindIndex(a => a.Id == action.Id);
        if (index != -1)
        {
            actions.RemoveAt(index);
            Actions = actions;
        }
    }

    // 显示器亮度同步设置
    public string? ControlMyMonitorPath
    {
        get => _configuration.Get<string?>(SettingsKey(nameof(ControlMyMonitorPath)), null);
        set => _configuration.Set(SettingsKey(nameof(ControlMyMonitorPath)), value);
    }

    public bool EnableMonitorBrightnessSync
    {
        get => _configuration.Get(SettingsKey(nameof(EnableMonitorBrightnessSync)), false);
        set => _configuration.Set(SettingsKey(nameof(EnableMonitorBrightnessSync)), value);
    }

    public List<string> SelectedMonitors
    {
        get => _configuration.Get(SettingsKey(nameof(SelectedMonitors)), new List<string>())!;
        set => _configuration.Set(SettingsKey(nameof(SelectedMonitors)), value);
    }

    public bool EnableSendMediaNotifications
    {
        get => _configuration.Get(SettingsKey(nameof(EnableSendMediaNotifications)), true);
        set => _configuration.Set(SettingsKey(nameof(EnableSendMediaNotifications)), value);
    }
}
