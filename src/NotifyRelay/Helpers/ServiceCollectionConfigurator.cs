using NotifyRelay.Data.AppDatabase;
using NotifyRelay.Data.AppDatabase.Models;
using NotifyRelay.Data.AppDatabase.Repository;
using NotifyRelay.Data.Contracts;
using NotifyRelay.Native;
using NotifyRelay.Platforms.Windows;
using NotifyRelay.Platforms.Windows.Services;
// AdbService / NotificationService / LocalNotificationListenerService / BaseActionService
// 保留在 NotifyRelay.Services 根命名空间
using NotifyRelay.Services;
using NotifyRelay.Services.Devices;
using NotifyRelay.Services.Notifications;
using NotifyRelay.Services.Infrastructure;
using NotifyRelay.Services.Media;
using NotifyRelay.Services.Overlay;
using NotifyRelay.Services.OverlayFeatures;
using NotifyRelay.Services.Protocol;
using NotifyRelay.Services.Settings;
using NotifyRelay.ViewModels;
using NotifyRelay.ViewModels.Settings;
using Serilog;
using ILogger = Microsoft.Extensions.Logging.ILogger;

namespace NotifyRelay.Helpers;

/// <summary>
/// 承载 DI 容器注册清单（原 AppLifecycleHelper.ConfigureServices 整段搬移）。
/// </summary>
internal static class ServiceCollectionConfigurator
{
    internal static void ConfigureServices(IServiceCollection services)
    {
        services.AddSingleton<ILogger>(sp => sp.GetRequiredService<ILogger<App>>())

        // Settings Services
        .AddSingleton<UserSettingsService>()
        .AddSingleton<IUserSettingsService>(sp => sp.GetRequiredService<UserSettingsService>())
        .AddSingleton<IGeneralSettingsService>(sp => sp.GetRequiredService<UserSettingsService>().GeneralSettingsService)
        .AddSingleton<IOverlaySettings>(sp => (IOverlaySettings)sp.GetRequiredService<UserSettingsService>().GeneralSettingsService)
        .AddSingleton<NotifyRelay.Worker.Configuration.IDeepSeekBalanceSettings, DeepSeekBalanceSettingsAccessor>()

        // Database and Repositories
        .AddSingleton<DatabaseContext>()
        .AddSingleton<SettingsRepository>()
        .AddSingleton<DeviceRepository>()
        .AddSingleton<RemoteAppRepository>()
        .AddSingleton<NotificationRepository>()
        .AddSingleton<FilterConfigRepository>()

        // Platform-specific services
        .AddWindowsServices()
        // Services
        // 1. 首先注册基础服务
        .AddSingleton<ISystemInfoService, SystemInfoService>()
        .AddSingleton<IDeviceManager, DeviceManager>()
        .AddSingleton<IAdbService, AdbService>()
        .AddSingleton<IScreenMirrorService, ScreenMirrorService>()
        .AddSingleton<IFileTransferService, FileTransferService>()
        .AddSingleton<IProtocolSender, ProtocolSender>()
        .AddSingleton<IClipboardService, ClipboardService>()
        .AddSingleton<IRemoteAppService, RemoteAppService>()

        // 3. 注册ProtocolRouter（ProtocolRouter 依赖 NetworkDriveMapper）
        .AddSingleton<Func<NetworkDriveMapper>>(sp => () => sp.GetRequiredService<NetworkDriveMapper>())
        .AddSingleton<ProtocolRouter>()
        .AddSingleton<HeartbeatProcessor>()

        // 设备状态唯一真源消费端与同步查询入口
        .AddSingleton<IDeviceSnapshotStore, DeviceSnapshotStore>()
        .AddSingleton<IDeviceDirectory, DeviceDirectory>()

        // 4. 注册INetworkService和工厂函数，它依赖ProtocolRouter
        .AddSingleton<INetworkService, NetworkService>()
        .AddSingleton<Func<INetworkService>>(sp => () => sp.GetRequiredService<INetworkService>())

        // 5. 注册ISessionManager，由INetworkService实现
        .AddSingleton<ISessionManager>(sp => (ISessionManager)sp.GetRequiredService<INetworkService>())

        // 6. 注册通知相关服务
        .AddNotificationServices()
        .AddSingleton<ILocalNotificationListenerService, LocalNotificationListenerService>()

        // 注册其他需要的工厂
        .AddSingleton<Func<IClipboardService>>(sp => () => sp.GetRequiredService<IClipboardService>())
        .AddSingleton<Func<IRemoteAppService>>(sp => () => sp.GetRequiredService<IRemoteAppService>())
        .AddSingleton<Func<IPlaybackService>>(sp => () => sp.GetRequiredService<IPlaybackService>())

        // 7. 注册IDiscoveryService，它依赖INetworkService
        .AddSingleton<IDiscoveryService, DiscoveryService>()

        // Worker Services
        .AddSingleton<NotifyRelay.Worker.Configuration.WorkerConfiguration>()
        .AddSingleton<NotifyRelay.Worker.Services.DeepSeekBalanceService>()
        .AddSingleton<NotifyRelay.Worker.Services.MonitorBrightnessService>()
        .AddSingleton<NotifyRelay.Worker.Services.DynamicLightingService>()

        // Audio Relay Service
        .AddSingleton<DeviceCtrl.AudioRelay.AudioRelayService>()

        // Overlay Render Service
        .AddSingleton<OverlayRenderService>()

        // 叠加层功能：登记后由 OverlayRenderService 主初始化按各自开关自动引导，
        // 新增功能只需在此追加一行登记，无需改动启动流程
        .AddSingleton<IOverlayFeature, KeyboardOverlayFeature>()
        .AddSingleton<IOverlayFeature, LogiBatteryOverlayFeature>()
        .AddSingleton<IOverlayFeature, HeartRateOverlayFeature>()
        .AddSingleton<IOverlayFeature, DeepSeekBalanceOverlayFeature>()

        // Heart Rate BLE Service
        .AddSingleton<NotifyRelay.Services.HeartRate.HeartRateBleService>()
        .AddSingleton<ViewModels.Settings.HeartRateViewModel>()

        // 罗技电池（LogiBattery）Provider 和 ViewModel
        .AddSingleton<LogiBatteryProvider>()
        .AddSingleton<ILogiBatteryProvider>(sp => sp.GetRequiredService<LogiBatteryProvider>())
        .AddSingleton<ViewModels.Settings.LogiBatteryViewModel>()

        // 时间浮窗（Clock）ViewModel
        .AddSingleton<ViewModels.Settings.ClockViewModel>()

        // DeepSeek 余额（覆盖层子页）ViewModel
        .AddSingleton<ViewModels.Settings.DeepSeekBalanceViewModel>()

        // ViewModels
        .AddSingleton<MainPageViewModel>()
        .AddSingleton<DevicesViewModel>()
        .AddSingleton<AppsViewModel>()
        .AddSingleton<LocalNotificationHistoryViewModel>();
    }
}
