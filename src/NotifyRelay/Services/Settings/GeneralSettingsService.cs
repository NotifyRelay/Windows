using CommunityToolkit.WinUI;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using NotifyRelay.Data.Contracts;
using NotifyRelay.Data.Enums;
using NotifyRelay.Data.Models.Actions;
using NotifyRelay.Utils.Serialization;
using Windows.UI;
using Windows.UI.ViewManagement;

namespace NotifyRelay.Services.Settings;

internal sealed partial class GeneralSettingsService : BaseObservableJsonSettings, IGeneralSettingsService
{
    private readonly UISettings _uiSettings = new();
    private bool _isApplyingTheme;

    public event EventHandler? ThemeChanged;

    public GeneralSettingsService(ISettingsSharingContext settingsSharingContext)
    {
        // Register root
        RegisterSettingsContext(settingsSharingContext);

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
        get => Get(StartupOptions.InTray);
        set => Set(value);
    }

    public Theme Theme
    {
        get => Get(Theme.Default);
        set
        {
            if (Set(value))
            {
                ApplyTheme(App.MainWindow, null, value);
                ThemeChanged?.Invoke(this, EventArgs.Empty);
            }
        }
    }

    public void ApplyTheme(Window? window = null, AppWindowTitleBar? titleBar = null, Theme? theme = null, bool callThemeModeChangedEvent = true)
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
            if (callThemeModeChangedEvent)
                ThemeChanged?.Invoke(null, EventArgs.Empty);
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

    public string RemoteStoragePath
    {
        get => Get(Constants.UserEnvironmentPaths.DefaultRemoteDevicePath)!;
        set => Set(value);
    }

    public string ReceivedFilesPath
    {
        get => Get(Constants.UserEnvironmentPaths.DownloadsPath)!;
        set => Set(value);
    }

    public string ScrcpyPath
    {
        get => Get(string.Empty)!;
        set => Set(value);
    }

    public string AdbPath
    {
        get => Get(string.Empty)!;
        set => Set(value);
    }

    public MediaMessageReceiveMode MediaMessageReceiveMode
    {
        get => Get(MediaMessageReceiveMode.AudioOnly);
        set => Set(value);
    }

    public List<BaseAction> Actions
    {
        get => Get<List<BaseAction>>([])!;
        set => Set(value);
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
        get => Get<string?>(null);
        set => Set(value);
    }

    public bool EnableMonitorBrightnessSync
    {
        get => Get(false);
        set => Set(value);
    }

    public List<string> SelectedMonitors
    {
        get => Get<List<string>>([])!;
        set => Set(value);
    }

    public string? DeepSeekApiToken
    {
        get => Get<string?>(null);
        set => Set(value);
    }

    public bool EnableDeepSeekBalanceMonitor
    {
        get => Get(false);
        set => Set(value);
    }

    public int DeepSeekBalancePollingInterval
    {
        get => Get(60000);
        set => Set(value);
    }

    public string? DeepSeekBalanceHistoryJson
    {
        get => Get<string?>(null);
        set => Set(value);
    }

    public bool DeepSeekBalanceHistoryCollapsed
    {
        get => Get(false);
        set => Set(value);
    }

    // 弹幕叠加层设置
    public bool DanmakuNotificationEnabled
    {
        get => Get(true);
        set => Set(value);
    }

    public bool DanmakuMediaCardEnabled
    {
        get => Get(true);
        set => Set(value);
    }

    public bool DanmakuSuperIslandEnabled
    {
        get => Get(true);
        set => Set(value);
    }

    public bool GamebarRelayEnabled
    {
        get => Get(false);
        set => Set(value);
    }

    public int DanmakuFontSizePercent
    {
        get => Get(50);
        set => Set(value);
    }

    public int DanmakuSpeed
    {
        get => Get(3);
        set => Set(value);
    }

    public int DanmakuOpacityPercent
    {
        get => Get(100);
        set => Set(value);
    }

    public int DanmakuDisplayAreaPercent
    {
        get => Get(100);
        set => Set(value);
    }

    public int DanmakuDensity
    {
        get => Get(0);
        set => Set(value);
    }

    public string DanmakuFontFamily
    {
        get => Get("Microsoft YaHei")!;
        set => Set(value);
    }

    public bool DanmakuBold
    {
        get => Get(true);
        set => Set(value);
    }

    public string DanmakuColor
    {
        get => Get("#FFFFFF")!;
        set => Set(value);
    }

    public bool DanmakuBorderEnabled
    {
        get => Get(true);
        set => Set(value);
    }

    public int DanmakuBorderThickness
    {
        get => Get(2);
        set => Set(value);
    }

    public string DanmakuBorderColor
    {
        get => Get("#000000")!;
        set => Set(value);
    }

    public bool DanmakuShadowEnabled
    {
        get => Get(true);
        set => Set(value);
    }

    public int DanmakuShadowDepth
    {
        get => Get(2);
        set => Set(value);
    }

    public int DanmakuShadowOpacity
    {
        get => Get(100);
        set => Set(value);
    }

    public string DanmakuShadowColor
    {
        get => Get("#000000")!;
        set => Set(value);
    }

    public int DanmakuDisplayScreenMode
    {
        get => Get(0);
        set => Set(value);
    }

    public int DanmakuPerformanceMode
    {
        get => Get(0);
        set => Set(value);
    }

    // 心率覆盖层设置
    public bool HeartRateOverlayEnabled
    {
        get => Get(false);
        set => Set(value);
    }

    public int HeartRateStyle
    {
        get => Get(1);
        set => Set(value);
    }

    public string HeartRateTargetScreen
    {
        get => Get("PRIMARY")!;
        set => Set(value);
    }

    public int HeartRateXPercent
    {
        get => Get(90);
        set => Set(value);
    }

    public int HeartRateYPercent
    {
        get => Get(85);
        set => Set(value);
    }

    public string HeartRateColor
    {
        get => Get("#FFFFFF")!;
        set => Set(value);
    }

    public float HeartRateTextOutlineWidth
    {
        get => Get(2f);
        set => Set(Math.Clamp(value, 0.1f, 3f));
    }

    // 动态光效设置
    public bool EnableDynamicLighting
    {
        get => Get(false);
        set => Set(value);
    }

    public bool EnableAutoRGB
    {
        get => Get(false);
        set => Set(value);
    }

    public double DynamicLightingBrightness
    {
        get => Get(1.0);
        set => Set(value);
    }

    public string? DynamicLightingColor
    {
        get => Get<string?>(null);
        set => Set(value);
    }

    public string? DynamicLightingEffect
    {
        get => Get<string?>(null);
        set => Set(value);
    }

    public int AutoRGBUpdateInterval
    {
        get => Get(5000);
        set => Set(value);
    }

    public bool EnableSendMediaNotifications
    {
        get => Get(true);
        set => Set(value);
    }
}
