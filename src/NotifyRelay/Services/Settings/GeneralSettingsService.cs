using CommunityToolkit.WinUI;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using NotifyRelay.Data.Configuration;
using NotifyRelay.Data.Contracts;
using NotifyRelay.Data.Enums;
using NotifyRelay.Data.Models.Actions;
using NotifyRelay.Services.Overlay;
using Windows.UI;
using Windows.UI.ViewManagement;

namespace NotifyRelay.Services.Settings;

internal sealed class GeneralSettingsService : IGeneralSettingsService, IOverlaySettings
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

    public string? DeepSeekApiToken
    {
        get => _configuration.Get<string?>(SettingsKey(nameof(DeepSeekApiToken)), null);
        set => _configuration.Set(SettingsKey(nameof(DeepSeekApiToken)), value);
    }

    public bool EnableDeepSeekBalanceMonitor
    {
        get => _configuration.Get(SettingsKey(nameof(EnableDeepSeekBalanceMonitor)), false);
        set => _configuration.Set(SettingsKey(nameof(EnableDeepSeekBalanceMonitor)), value);
    }

    public int DeepSeekBalancePollingInterval
    {
        get => _configuration.Get(SettingsKey(nameof(DeepSeekBalancePollingInterval)), 60000);
        set => _configuration.Set(SettingsKey(nameof(DeepSeekBalancePollingInterval)), value);
    }

    public string? DeepSeekBalanceHistoryJson
    {
        get => _configuration.Get<string?>(SettingsKey(nameof(DeepSeekBalanceHistoryJson)), null);
        set => _configuration.Set(SettingsKey(nameof(DeepSeekBalanceHistoryJson)), value);
    }

    public bool DeepSeekBalanceHistoryCollapsed
    {
        get => _configuration.Get(SettingsKey(nameof(DeepSeekBalanceHistoryCollapsed)), false);
        set => _configuration.Set(SettingsKey(nameof(DeepSeekBalanceHistoryCollapsed)), value);
    }

    // 弹幕叠加层设置
    public bool DanmakuNotificationEnabled
    {
        get => _configuration.Get(SettingsKey(nameof(DanmakuNotificationEnabled)), true);
        set => _configuration.Set(SettingsKey(nameof(DanmakuNotificationEnabled)), value);
    }

    public bool DanmakuMediaCardEnabled
    {
        get => _configuration.Get(SettingsKey(nameof(DanmakuMediaCardEnabled)), true);
        set => _configuration.Set(SettingsKey(nameof(DanmakuMediaCardEnabled)), value);
    }

    public bool DanmakuSuperIslandEnabled
    {
        get => _configuration.Get(SettingsKey(nameof(DanmakuSuperIslandEnabled)), true);
        set => _configuration.Set(SettingsKey(nameof(DanmakuSuperIslandEnabled)), value);
    }

    public bool GamebarRelayEnabled
    {
        get => _configuration.Get(SettingsKey(nameof(GamebarRelayEnabled)), false);
        set => _configuration.Set(SettingsKey(nameof(GamebarRelayEnabled)), value);
    }

    public int DanmakuFontSizePercent
    {
        get => _configuration.Get(SettingsKey(nameof(DanmakuFontSizePercent)), 50);
        set => _configuration.Set(SettingsKey(nameof(DanmakuFontSizePercent)), value);
    }

    public int DanmakuSpeed
    {
        get => _configuration.Get(SettingsKey(nameof(DanmakuSpeed)), 3);
        set => _configuration.Set(SettingsKey(nameof(DanmakuSpeed)), value);
    }

    public int DanmakuOpacityPercent
    {
        get => _configuration.Get(SettingsKey(nameof(DanmakuOpacityPercent)), 100);
        set => _configuration.Set(SettingsKey(nameof(DanmakuOpacityPercent)), value);
    }

    public int DanmakuDisplayAreaPercent
    {
        get => _configuration.Get(SettingsKey(nameof(DanmakuDisplayAreaPercent)), 100);
        set => _configuration.Set(SettingsKey(nameof(DanmakuDisplayAreaPercent)), value);
    }

    public int DanmakuDensity
    {
        get => _configuration.Get(SettingsKey(nameof(DanmakuDensity)), 0);
        set => _configuration.Set(SettingsKey(nameof(DanmakuDensity)), value);
    }

    public string DanmakuFontFamily
    {
        get => _configuration.Get(SettingsKey(nameof(DanmakuFontFamily)), "Microsoft YaHei")!;
        set => _configuration.Set(SettingsKey(nameof(DanmakuFontFamily)), value);
    }

    public bool DanmakuBold
    {
        get => _configuration.Get(SettingsKey(nameof(DanmakuBold)), true);
        set => _configuration.Set(SettingsKey(nameof(DanmakuBold)), value);
    }

    public string DanmakuColor
    {
        get => _configuration.Get(SettingsKey(nameof(DanmakuColor)), "#FFFFFF")!;
        set => _configuration.Set(SettingsKey(nameof(DanmakuColor)), value);
    }

    public bool DanmakuBorderEnabled
    {
        get => _configuration.Get(SettingsKey(nameof(DanmakuBorderEnabled)), true);
        set => _configuration.Set(SettingsKey(nameof(DanmakuBorderEnabled)), value);
    }

    public int DanmakuBorderThickness
    {
        get => _configuration.Get(SettingsKey(nameof(DanmakuBorderThickness)), 2);
        set => _configuration.Set(SettingsKey(nameof(DanmakuBorderThickness)), value);
    }

    public string DanmakuBorderColor
    {
        get => _configuration.Get(SettingsKey(nameof(DanmakuBorderColor)), "#000000")!;
        set => _configuration.Set(SettingsKey(nameof(DanmakuBorderColor)), value);
    }

    public bool DanmakuShadowEnabled
    {
        get => _configuration.Get(SettingsKey(nameof(DanmakuShadowEnabled)), true);
        set => _configuration.Set(SettingsKey(nameof(DanmakuShadowEnabled)), value);
    }

    public int DanmakuShadowDepth
    {
        get => _configuration.Get(SettingsKey(nameof(DanmakuShadowDepth)), 2);
        set => _configuration.Set(SettingsKey(nameof(DanmakuShadowDepth)), value);
    }

    public int DanmakuShadowOpacity
    {
        get => _configuration.Get(SettingsKey(nameof(DanmakuShadowOpacity)), 100);
        set => _configuration.Set(SettingsKey(nameof(DanmakuShadowOpacity)), value);
    }

    public string DanmakuShadowColor
    {
        get => _configuration.Get(SettingsKey(nameof(DanmakuShadowColor)), "#000000")!;
        set => _configuration.Set(SettingsKey(nameof(DanmakuShadowColor)), value);
    }

    public int DanmakuDisplayScreenMode
    {
        get => _configuration.Get(SettingsKey(nameof(DanmakuDisplayScreenMode)), 0);
        set => _configuration.Set(SettingsKey(nameof(DanmakuDisplayScreenMode)), value);
    }

    public int DanmakuPerformanceMode
    {
        get => _configuration.Get(SettingsKey(nameof(DanmakuPerformanceMode)), 0);
        set => _configuration.Set(SettingsKey(nameof(DanmakuPerformanceMode)), value);
    }

    // 心率覆盖层设置
    public bool HeartRateOverlayEnabled
    {
        get => _configuration.Get(SettingsKey(nameof(HeartRateOverlayEnabled)), false);
        set => _configuration.Set(SettingsKey(nameof(HeartRateOverlayEnabled)), value);
    }

    public int HeartRateStyle
    {
        get => _configuration.Get(SettingsKey(nameof(HeartRateStyle)), 1);
        set => _configuration.Set(SettingsKey(nameof(HeartRateStyle)), value);
    }

    public string HeartRateTargetScreen
    {
        get => _configuration.Get(SettingsKey(nameof(HeartRateTargetScreen)), "PRIMARY")!;
        set => _configuration.Set(SettingsKey(nameof(HeartRateTargetScreen)), value);
    }

    public int HeartRateXPercent
    {
        get => _configuration.Get(SettingsKey(nameof(HeartRateXPercent)), 90);
        set => _configuration.Set(SettingsKey(nameof(HeartRateXPercent)), value);
    }

    public int HeartRateYPercent
    {
        get => _configuration.Get(SettingsKey(nameof(HeartRateYPercent)), 85);
        set => _configuration.Set(SettingsKey(nameof(HeartRateYPercent)), value);
    }

    public string HeartRateColor
    {
        get => _configuration.Get(SettingsKey(nameof(HeartRateColor)), "#FFFFFF")!;
        set => _configuration.Set(SettingsKey(nameof(HeartRateColor)), value);
    }

    public float HeartRateTextOutlineWidth
    {
        get => _configuration.Get(SettingsKey(nameof(HeartRateTextOutlineWidth)), 2f);
        set => _configuration.Set(SettingsKey(nameof(HeartRateTextOutlineWidth)), Math.Clamp(value, 0.1f, 3f));
    }

    public bool HeartRateAlertEnabled
    {
        get => _configuration.Get(SettingsKey(nameof(HeartRateAlertEnabled)), false);
        set => _configuration.Set(SettingsKey(nameof(HeartRateAlertEnabled)), value);
    }

    public int HeartRateLowAlert
    {
        get => _configuration.Get(SettingsKey(nameof(HeartRateLowAlert)), 50);
        set => _configuration.Set(SettingsKey(nameof(HeartRateLowAlert)), value);
    }

    public int HeartRateHighAlert
    {
        get => _configuration.Get(SettingsKey(nameof(HeartRateHighAlert)), 120);
        set => _configuration.Set(SettingsKey(nameof(HeartRateHighAlert)), value);
    }

    public int HeartRateSpikeDelta
    {
        get => _configuration.Get(SettingsKey(nameof(HeartRateSpikeDelta)), 20);
        set => _configuration.Set(SettingsKey(nameof(HeartRateSpikeDelta)), value);
    }

    public float HeartRateScale
    {
        get => _configuration.Get(SettingsKey(nameof(HeartRateScale)), 1f);
        set => _configuration.Set(SettingsKey(nameof(HeartRateScale)), Math.Clamp(value, 0.5f, 2f));
    }

    // 动态光效设置
    public bool EnableDynamicLighting
    {
        get => _configuration.Get(SettingsKey(nameof(EnableDynamicLighting)), false);
        set => _configuration.Set(SettingsKey(nameof(EnableDynamicLighting)), value);
    }

    public bool EnableAutoRGB
    {
        get => _configuration.Get(SettingsKey(nameof(EnableAutoRGB)), false);
        set => _configuration.Set(SettingsKey(nameof(EnableAutoRGB)), value);
    }

    public double DynamicLightingBrightness
    {
        get => _configuration.Get(SettingsKey(nameof(DynamicLightingBrightness)), 1.0);
        set => _configuration.Set(SettingsKey(nameof(DynamicLightingBrightness)), value);
    }

    public string? DynamicLightingColor
    {
        get => _configuration.Get<string?>(SettingsKey(nameof(DynamicLightingColor)), null);
        set => _configuration.Set(SettingsKey(nameof(DynamicLightingColor)), value);
    }

    public string? DynamicLightingEffect
    {
        get => _configuration.Get<string?>(SettingsKey(nameof(DynamicLightingEffect)), null);
        set => _configuration.Set(SettingsKey(nameof(DynamicLightingEffect)), value);
    }

    public int AutoRGBUpdateInterval
    {
        get => _configuration.Get(SettingsKey(nameof(AutoRGBUpdateInterval)), 5000);
        set => _configuration.Set(SettingsKey(nameof(AutoRGBUpdateInterval)), value);
    }

    public bool EnableSendMediaNotifications
    {
        get => _configuration.Get(SettingsKey(nameof(EnableSendMediaNotifications)), true);
        set => _configuration.Set(SettingsKey(nameof(EnableSendMediaNotifications)), value);
    }
}
