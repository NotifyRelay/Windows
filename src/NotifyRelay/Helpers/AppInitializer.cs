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
/// 承载应用初始化子任务（原 AppLifecycleHelper 分组 D 方法整段搬移）。
/// 调用顺序仍由 AppLifecycleHelper.InitializeAppComponentsAsync 编排。
/// </summary>
internal static class AppInitializer
{
    /// <summary>
    /// 步骤17b：Rust 持久化收尾（start_core 已传入本机 uuid）
    /// - GetLocalUuid 触发自动落盘并校验
    /// - 平台表 DeviceId 与库对齐
    /// - 清理旧平台存储：LocalDeviceEntity.StateJson 值、RemoteDeviceEntity.SharedSecret 列值
    /// </summary>
    internal static async Task FinalizeRustPersistenceAsync(ILogger logger, LocalDeviceEntity localDevice, bool allMigrationsSucceeded)
    {
        var rustUuid = NativeCore.GetLocalUuid();
        if (string.IsNullOrEmpty(rustUuid))
        {
            logger.LogWarning("步骤17b：Rust 持久化未就绪，暂缓清理旧平台存储");
            return;
        }
        var repo = Ioc.Default.GetRequiredService<DeviceRepository>();

        if (rustUuid != localDevice.DeviceId)
        {
            logger.LogInformation("步骤17b：UUID 以 Rust 持久化为准: {rustUuid} (原: {oldId})", rustUuid, localDevice.DeviceId);
            var oldId = localDevice.DeviceId;
            localDevice.DeviceId = rustUuid;
            if (!repo.RenameLocalDeviceKey(oldId, rustUuid))
            {
                logger.LogWarning("步骤17b：平台主键更新失败，保持旧 DeviceId 以维护内存与数据库一致性");
                localDevice.DeviceId = oldId;
                return;
            }
        }

        if (!string.IsNullOrEmpty(localDevice.StateJson))
        {
            localDevice.StateJson = string.Empty;
            repo.AddOrUpdateLocalDevice(localDevice);
            logger.LogInformation("步骤17b：已清理旧加密状态 blob（密钥由 Rust 私有库持有）");
        }

        // 远程设备旧密钥列值已全部迁移至 Rust，清空平台存储
        // 仅当所有设备迁移均成功且持久化已确认后才清理，否则保留旧密钥允许下次启动重试
        if (allMigrationsSucceeded)
        {
            int cleared = repo.ClearRemoteSecrets();
            if (cleared > 0)
            {
                logger.LogInformation("步骤17b：已清空 {count} 条旧设备密钥记录", cleared);
            }
        }
        else
        {
            logger.LogWarning("步骤17b：存在迁移失败的设备，跳过清空旧密钥列，下次启动将重试");
        }

        await Task.CompletedTask;
    }

    internal static async Task RegisterWindowsNotificationAsync(ILogger logger)
    {
        try
        {
            var handler = Ioc.Default.GetRequiredService<IPlatformNotificationHandler>();
            await handler.RegisterForNotifications();
            logger.LogInformation("Windows通知注册成功");
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "注册Windows通知失败");
        }
    }

    internal static async Task InitRustCoreAsync(ILogger logger)
    {
        try
        {
            NativeCore.Initialize();
            NativeCore.SetLogCallback(logger);
            NativeCore.ProtocolRouter = Ioc.Default.GetRequiredService<ProtocolRouter>();
            NativeCore.DeviceManager = Ioc.Default.GetRequiredService<IDeviceManager>();
            NativeCore.RegisterCallbacks();
            NativeCore.NetworkService = (NetworkService?)Ioc.Default.GetService<INetworkService>();
            NativeCore.HeartbeatProcessor = Ioc.Default.GetService<HeartbeatProcessor>();
            logger.LogInformation("Rust Core 初始化完成，回调已注册");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "初始化 Rust Core 失败");
        }
    }

    internal static async Task StartLocalSocketRelayAsync(ILogger logger)
    {
        try
        {
            var socketLogger = Ioc.Default.GetRequiredService<ILogger>();
            LocalSocketRelayServer.SetLogger(socketLogger);
            LocalSocketRelayServer.Start();
            logger.LogInformation("LocalSocketRelayServer启动完成");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "启动LocalSocketRelayServer失败");
        }
    }

    internal static async Task InitWorkerConfigAsync(ILogger logger)
    {
        try
        {
            var config = Ioc.Default.GetRequiredService<NotifyRelay.Worker.Configuration.WorkerConfiguration>();
            var settings = Ioc.Default.GetRequiredService<IGeneralSettingsService>();

            config.ControlMyMonitorPath = settings.ControlMyMonitorPath;
            config.SelectedMonitors = settings.SelectedMonitors;
            config.EnableMonitorBrightnessSync = settings.EnableMonitorBrightnessSync;
            config.DynamicLightingBrightness = settings.DynamicLightingBrightness;
            config.DynamicLightingColor = settings.DynamicLightingColor;
            config.DynamicLightingEffect = settings.DynamicLightingEffect;
            config.EnableAutoRGB = settings.EnableAutoRGB;
            config.AutoRGBUpdateInterval = settings.AutoRGBUpdateInterval;

            logger.LogInformation("Worker 服务配置已初始化");

            // 按已持久化的设置开关，在应用启动时自动拉起对应的 Worker 服务
            await StartWorkerServicesAsync(logger, settings);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "初始化 Worker 服务配置失败");
        }
    }

    /// <summary>
    /// 应用启动时，依据已开启的设置项自动启动对应的 Worker 服务。
    /// </summary>
    internal static async Task StartWorkerServicesAsync(ILogger logger, IGeneralSettingsService settings)
    {
        // 动态光效依赖 WinRT UI 亲和 API（DeviceWatcher / LampArray），需在 UI 线程启动
        if (settings.EnableDynamicLighting)
        {
            try
            {
                var lightingService = Ioc.Default.GetRequiredService<NotifyRelay.Worker.Services.DynamicLightingService>();
                await AppLifecycleHelper.RunOnUiThreadAsync(lightingService.Initialize);
                logger.LogInformation("动态光效服务已按设置自动启动");
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "自动启动动态光效服务失败");
            }
        }

        if (settings.EnableMonitorBrightnessSync)
        {
            try
            {
                var brightnessService = Ioc.Default.GetRequiredService<NotifyRelay.Worker.Services.MonitorBrightnessService>();
                brightnessService.StartSync();
                logger.LogInformation("显示器亮度同步服务已按设置自动启动");
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "自动启动显示器亮度同步服务失败");
            }
        }
    }

    internal static async Task InitAudioRelayAsync(ILogger logger)
    {
        try
        {
            var audioRelayService = Ioc.Default.GetRequiredService<DeviceCtrl.AudioRelay.AudioRelayService>();
            logger.LogInformation("音频中继服务已就绪");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "初始化音频中继服务失败");
        }
    }

    internal static async Task StartLocalNotificationListenerAsync(ILogger logger)
    {
        try
        {
            var localListener = Ioc.Default.GetRequiredService<ILocalNotificationListenerService>();
            localListener.Start();
            logger.LogInformation("本地通知监听服务启动完成");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "启动本地通知监听服务失败");
        }
    }
}
