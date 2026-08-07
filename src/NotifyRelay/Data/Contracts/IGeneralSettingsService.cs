using Microsoft.UI.Windowing;
using NotifyRelay.Data.Enums;
using NotifyRelay.Data.Models.Actions;

namespace NotifyRelay.Data.Contracts;

public interface IGeneralSettingsService
{
    /// <summary>
    /// Gets or sets the startup option for the application.
    /// </summary>
    StartupOptions StartupOption { get; set; }

    /// <summary>
    /// Gets or sets the theme for the application.
    /// </summary>
    Theme Theme { get; set; }

    /// <summary>
    /// Applies the theme to a specific window or the main window
    /// </summary>
    /// <param name="window">Optional window to apply theme to</param>
    /// <param name="titleBar">Optional titlebar to apply theme to</param>
    /// <param name="theme">Optional specific theme to apply</param>
    void ApplyTheme(Window? window = null, AppWindowTitleBar? titleBar = null, Theme? theme = null);

    /// <summary>
    /// Gets or sets the path for scrcpy.
    /// </summary>
    string ScrcpyPath { get; set; }

    /// <summary>
    /// Gets or sets the path for adb.
    /// </summary>
    string AdbPath { get; set; }

    /// <summary>
    /// Gets or sets the receive mode for media control messages.
    /// </summary>
    MediaMessageReceiveMode MediaMessageReceiveMode { get; set; }

    /// <summary>
    /// Gets or sets the path for remote storage.
    /// </summary>
    string RemoteStoragePath { get; set; }

    /// <summary>
    /// Gets or sets the path for received files.
    /// </summary>
    string ReceivedFilesPath { get; set; }

    /// <summary>
    /// Gets or sets the list of custom actions.
    /// </summary>
    List<BaseAction> Actions { get; set; }

    /// <summary>
    /// Adds a new action to the settings.
    /// </summary>
    void AddAction(BaseAction action);

    /// <summary>
    /// Updates an existing action in the settings.
    /// </summary>
    void UpdateAction(BaseAction action);

    /// <summary>
    /// Removes an action from the settings.
    /// </summary>
    void RemoveAction(BaseAction action);

    /// <summary>
    /// Gets or sets the path for ControlMyMonitor.exe.
    /// </summary>
    string? ControlMyMonitorPath { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether monitor brightness sync is enabled.
    /// </summary>
    bool EnableMonitorBrightnessSync { get; set; }

    /// <summary>
    /// Gets or sets the list of selected monitors for brightness sync.
    /// </summary>
    List<string> SelectedMonitors { get; set; }

    /// <summary>
    /// Gets or sets the DeepSeek API token for balance monitoring.
    /// </summary>
    string? DeepSeekApiToken { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether DeepSeek balance monitoring is enabled.
    /// </summary>
    bool EnableDeepSeekBalanceMonitor { get; set; }

    /// <summary>
    /// Gets or sets the DeepSeek balance polling interval in milliseconds.
    /// </summary>
    int DeepSeekBalancePollingInterval { get; set; }

    /// <summary>
    /// Gets or sets the DeepSeek balance history as JSON string.
    /// </summary>
    string? DeepSeekBalanceHistoryJson { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether DeepSeek balance history should be collapsed.
    /// </summary>
    bool DeepSeekBalanceHistoryCollapsed { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether danmaku notification overlay is enabled.
    /// </summary>
    bool DanmakuNotificationEnabled { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether media card overlay is enabled.
    /// </summary>
    bool DanmakuMediaCardEnabled { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether SuperIsland card overlay is enabled.
    /// </summary>
    bool DanmakuSuperIslandEnabled { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether Gamebar relay is forced enabled.
    /// </summary>
    bool GamebarRelayEnabled { get; set; }

    int DanmakuFontSizePercent { get; set; }
    int DanmakuSpeed { get; set; }
    int DanmakuOpacityPercent { get; set; }
    int DanmakuDisplayAreaPercent { get; set; }
    int DanmakuDensity { get; set; }
    string DanmakuFontFamily { get; set; }
    bool DanmakuBold { get; set; }
    string DanmakuColor { get; set; }
    bool DanmakuBorderEnabled { get; set; }
    int DanmakuBorderThickness { get; set; }
    string DanmakuBorderColor { get; set; }
    bool DanmakuShadowEnabled { get; set; }
    int DanmakuShadowDepth { get; set; }
    int DanmakuShadowOpacity { get; set; }
    string DanmakuShadowColor { get; set; }

    /// <summary>
    /// Gets or sets the danmaku multi-screen display mode (0=primary, 1=all, 2=mouse, 3=span).
    /// </summary>
    int DanmakuDisplayScreenMode { get; set; }

    /// <summary>
    /// Gets or sets the danmaku performance tier (0=fluent, 1=balanced, 2=gaming).
    /// </summary>
    int DanmakuPerformanceMode { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether heart rate overlay is enabled.
    /// </summary>
    bool HeartRateOverlayEnabled { get; set; }

    /// <summary>
    /// Gets or sets the heart rate display style flags (1=text, 2=card, 4=heart shape; combinable).
    /// </summary>
    int HeartRateStyle { get; set; }

    /// <summary>
    /// Gets or sets the target screen device name for heart rate overlay ("PRIMARY" for primary screen).
    /// </summary>
    string HeartRateTargetScreen { get; set; }

    /// <summary>
    /// Gets or sets the heart rate overlay X position percent (0-100).
    /// </summary>
    int HeartRateXPercent { get; set; }

    /// <summary>
    /// Gets or sets the heart rate overlay Y position percent (0-100).
    /// </summary>
    int HeartRateYPercent { get; set; }

    /// <summary>
    /// Gets or sets the heart rate text color (#RRGGBB).
    /// </summary>
    string HeartRateColor { get; set; }

    /// <summary>
    /// Gets or sets the heart rate text outline width in pixels (clamped to 0.1-3).
    /// Outline color is the inverse of the text color.
    /// </summary>
    float HeartRateTextOutlineWidth { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the heart shape beats faster on abnormal heart rate.
    /// </summary>
    bool HeartRateAlertEnabled { get; set; }

    /// <summary>
    /// Gets or sets the low heart rate threshold (BPM) that triggers faster beating.
    /// </summary>
    int HeartRateLowAlert { get; set; }

    /// <summary>
    /// Gets or sets the high heart rate threshold (BPM) that triggers faster beating.
    /// </summary>
    int HeartRateHighAlert { get; set; }

    /// <summary>
    /// Gets or sets the spike threshold (BPM above recent average) that triggers faster beating.
    /// </summary>
    int HeartRateSpikeDelta { get; set; }

    /// <summary>
    /// Gets or sets the overall heart rate display scale (clamped to 0.5-2).
    /// Affects heart shape, text, card size and outline proportionally.
    /// </summary>
    float HeartRateScale { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether dynamic lighting is enabled.
    /// </summary>
    bool EnableDynamicLighting { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether AutoRGB mode is enabled.
    /// </summary>
    bool EnableAutoRGB { get; set; }

    /// <summary>
    /// Gets or sets the dynamic lighting brightness level (0-1).
    /// </summary>
    double DynamicLightingBrightness { get; set; }

    /// <summary>
    /// Gets or sets the dynamic lighting color.
    /// </summary>
    string? DynamicLightingColor { get; set; }

    /// <summary>
    /// Gets or sets the selected dynamic lighting effect.
    /// </summary>
    string? DynamicLightingEffect { get; set; }

    /// <summary>
    /// Gets or sets the AutoRGB update interval in milliseconds.
    /// </summary>
    int AutoRGBUpdateInterval { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether to send media notifications to connected devices.
    /// </summary>
    bool EnableSendMediaNotifications { get; set; }
}
