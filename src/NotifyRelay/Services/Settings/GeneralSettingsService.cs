using CommunityToolkit.WinUI;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using NotifyRelay.Data.AppDatabase.Repository;
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
    private readonly SettingsRepository _settings;
    private readonly UISettings _uiSettings = new();
    private bool _isApplyingTheme;

    public GeneralSettingsService(SettingsRepository settings)
    {
        _settings = settings;

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

    public StartupOptions StartupOption
    {
        get => _settings.Get(nameof(StartupOption), StartupOptions.InTray);
        set => _settings.Set(nameof(StartupOption), value);
    }

    public Theme Theme
    {
        get => _settings.Get(nameof(Theme), Theme.Default);
        set
        {
            if (_settings.Set(nameof(Theme), value))
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
        get => _settings.Get(nameof(ReceivedFilesPath), Constants.UserEnvironmentPaths.DownloadsPath);
        set => _settings.Set(nameof(ReceivedFilesPath), value);
    }

    public string ScrcpyPath
    {
        get => _settings.Get(nameof(ScrcpyPath), string.Empty);
        set => _settings.Set(nameof(ScrcpyPath), value);
    }

    public string AdbPath
    {
        get => _settings.Get(nameof(AdbPath), string.Empty);
        set => _settings.Set(nameof(AdbPath), value);
    }

    public MediaMessageReceiveMode MediaMessageReceiveMode
    {
        get => _settings.Get(nameof(MediaMessageReceiveMode), MediaMessageReceiveMode.AudioOnly);
        set => _settings.Set(nameof(MediaMessageReceiveMode), value);
    }

    public List<BaseAction> Actions
    {
        get => _settings.Get(nameof(Actions), new List<BaseAction>());
        set => _settings.Set(nameof(Actions), value);
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
        get => _settings.Get<string?>(nameof(ControlMyMonitorPath), null);
        set => _settings.Set(nameof(ControlMyMonitorPath), value);
    }

    public bool EnableMonitorBrightnessSync
    {
        get => _settings.Get(nameof(EnableMonitorBrightnessSync), false);
        set => _settings.Set(nameof(EnableMonitorBrightnessSync), value);
    }

    public List<string> SelectedMonitors
    {
        get => _settings.Get(nameof(SelectedMonitors), new List<string>());
        set => _settings.Set(nameof(SelectedMonitors), value);
    }

    public bool EnableSendMediaNotifications
    {
        get => _settings.Get(nameof(EnableSendMediaNotifications), true);
        set => _settings.Set(nameof(EnableSendMediaNotifications), value);
    }
}
