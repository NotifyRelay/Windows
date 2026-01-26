using NotifyRelay.Data.AppDatabase;
using NotifyRelay.Data.AppDatabase.Repository;
using NotifyRelay.Data.Contracts;
using NotifyRelay.Models;
#if WINDOWS
using NotifyRelay.Platforms.Windows;
using NotifyRelay.Platforms.Windows.Services;
#else
using NotifyRelay.Platforms.Desktop;
#endif
using NotifyRelay.Services;
using NotifyRelay.Services.Settings;
using NotifyRelay.Services.Socket;
using NotifyRelay.ViewModels;
using NotifyRelay.ViewModels.Settings;
using Serilog;
using ILogger = Microsoft.Extensions.Logging.ILogger;

namespace NotifyRelay.Helpers;

/// <summary>
/// Provides static helper to manage app lifecycle.
/// </summary>
public static class AppLifecycleHelper
{
    /// <summary>
    /// Gets application package version.
    /// </summary>
    public static Version AppVersion { get; } =
        new(Package.Current.Id.Version.Major, Package.Current.Id.Version.Minor, Package.Current.Id.Version.Build, Package.Current.Id.Version.Revision);

    public static async Task InitializeAppComponentsAsync()
    {
        var logger = Ioc.Default.GetRequiredService<ILogger<App>>();
        logger.LogInformation("开始初始化应用组件...");
        
        // 1. 首先初始化数据库上下文，确保数据库连接稳定
        logger.LogInformation("步骤1：获取DatabaseContext...");
        var databaseContext = Ioc.Default.GetRequiredService<DatabaseContext>();
        logger.LogInformation("DatabaseContext获取成功");
        
        logger.LogInformation("步骤2：获取DeviceRepository...");
        var deviceRepository = Ioc.Default.GetRequiredService<DeviceRepository>();
        logger.LogInformation("DeviceRepository获取成功");
        
        // 2. 预热数据库，确保表结构正确
        logger.LogInformation("步骤3：预热数据库，获取本地设备...");
        var localDevice = deviceRepository.GetLocalDevice();
        if (localDevice is null)
        {
            logger.LogInformation("数据库预热：本地设备不存在，将在后续生成");
        }
        else
        {
            logger.LogInformation("数据库预热：找到本地设备，DeviceId: {deviceId}", localDevice.DeviceId);
        }
        logger.LogInformation("数据库预热完成");
        
        // 3. 获取其他服务
        logger.LogInformation("步骤4：获取NetworkService...");
        var networkService = Ioc.Default.GetRequiredService<INetworkService>();
        logger.LogInformation("NetworkService获取成功");
        
        logger.LogInformation("步骤5：获取DiscoveryService...");
        var discoveryService = Ioc.Default.GetRequiredService<IDiscoveryService>();
        logger.LogInformation("DiscoveryService获取成功");
        
        logger.LogInformation("步骤6：获取NotificationService...");
        var notificationService = Ioc.Default.GetRequiredService<INotificationService>();
        logger.LogInformation("NotificationService获取成功");
        
        logger.LogInformation("步骤7：获取DeviceManager...");
        var deviceManager = Ioc.Default.GetRequiredService<IDeviceManager>();
        logger.LogInformation("DeviceManager获取成功");
        
        logger.LogInformation("步骤8：获取AdbService...");
        var adbService = Ioc.Default.GetRequiredService<IAdbService>();
        logger.LogInformation("AdbService获取成功");
        
        logger.LogInformation("步骤9：获取PlaybackService...");
        var playbackService = Ioc.Default.GetRequiredService<IPlaybackService>();
        logger.LogInformation("PlaybackService获取成功");
        
        logger.LogInformation("步骤10：获取ActionService...");
        var actionService = Ioc.Default.GetRequiredService<IActionService>();
        logger.LogInformation("ActionService获取成功");
        
        logger.LogInformation("步骤11：获取UpdateService...");
        var updateService = Ioc.Default.GetRequiredService<IUpdateService>();
        logger.LogInformation("UpdateService获取成功");

#if WINDOWS
        logger.LogInformation("步骤12：获取并注册WindowsNotificationHandler...");
        var windowsNotificationHandler = Ioc.Default.GetRequiredService<IPlatformNotificationHandler>();
        await windowsNotificationHandler.RegisterForNotifications();
        logger.LogInformation("WindowsNotificationHandler注册成功");
#endif
        
        // 3. 生成并初始化UUID，确保所有服务启动前UUID已可用
        logger.LogInformation("步骤13：开始生成并初始化UUID");
        localDevice = await deviceManager.GetLocalDeviceAsync();
        logger.LogInformation("步骤13：UUID初始化完成，DeviceId: {deviceId}", localDevice.DeviceId);
        
        // 4. 初始化设备管理器和通知服务
        logger.LogInformation("步骤14：初始化设备管理器...");
        await deviceManager.Initialize();
        logger.LogInformation("步骤14：设备管理器初始化完成");
        
        logger.LogInformation("步骤15：初始化通知服务...");
        notificationService.Initialize();
        logger.LogInformation("步骤15：通知服务初始化完成");

        // 5. 为LocalSocketRelayServer设置logger并启动服务器
        logger.LogInformation("步骤16：设置并启动LocalSocketRelayServer...");
        var socketLogger = Ioc.Default.GetRequiredService<ILogger>();
        LocalSocketRelayServer.SetLogger(socketLogger);
        LocalSocketRelayServer.Start();
        logger.LogInformation("步骤16：LocalSocketRelayServer启动完成");

        // 6. 启动各种服务
        logger.LogInformation("步骤17：开始启动各种服务...");
        await Task.WhenAll(
            networkService.StartServerAsync(),
            discoveryService.StartDiscoveryAsync(),
            playbackService.InitializeAsync(),
            actionService.InitializeAsync(),
            adbService.StartAsync(),
            updateService.CheckForUpdatesAsync()
        );
        logger.LogInformation("步骤17：所有服务启动完成");

        // 7. 完成初始化，关闭启动画面
        logger.LogInformation("步骤18：初始化完成，关闭启动画面");
        App.SplashScreenLoadingTCS?.TrySetResult();
        logger.LogInformation("应用组件初始化全部完成");
    } 

    public static IApplicationBuilder ConfigureApp(this App app, LaunchActivatedEventArgs args)
    {
        return app.CreateBuilder(args)
            .Configure(host => host
#if DEBUG
                // Switch to Development environment when running in DEBUG
                .UseEnvironment(Environments.Development)
#endif
                .UseLogging(configure: (context, logBuilder) =>
                {
                    // Configure log levels for different categories of logging
                    logBuilder
                        .SetMinimumLevel(
                            context.HostingEnvironment.IsDevelopment() ?
                                LogLevel.Debug :
                                LogLevel.Warning)

                        // Default filters for core Uno Platform namespaces
                        .CoreLogLevel(LogLevel.Warning);

                }, enableUnoLogging: true)
                .UseSerilog(
                    consoleLoggingEnabled: true,
                    fileLoggingEnabled: true,
                    configureLogger: config =>
                    {
                        config.WriteTo.File(
                            Path.Combine(ApplicationData.Current.LocalFolder.Path, "Logs", "Log_.log"),
                            rollingInterval: RollingInterval.Day,
                            retainedFileCountLimit: 7
                        );
                    }
                )
                .UseConfiguration(configure: configBuilder =>
                    configBuilder
                        .EmbeddedSource<App>()
                        .Section<AppConfig>()
                )
                .UseLocalization()
                .ConfigureServices((context, services) => services

                .AddSingleton<ILogger>(sp => sp.GetRequiredService<ILogger<App>>())

                // Settings Services
                .AddSingleton<IUserSettingsService, UserSettingsService>()
                .AddSingleton<IGeneralSettingsService, GeneralSettingsService>(sp => new GeneralSettingsService(((UserSettingsService)sp.GetRequiredService<IUserSettingsService>()).GetSharingContext()))

                // Database and Repositories
                .AddSingleton<DatabaseContext>()
                .AddSingleton<DeviceRepository>()
                .AddSingleton<RemoteAppRepository>()
                .AddSingleton<NotificationRepository>()

                // Platform-specific services
#if WINDOWS
                .AddWindowsServices()
#else
                .AddDesktopServices()
#endif
                // Services
                // 1. 首先注册基础服务
                .AddSingleton<ISystemInfoService, SystemInfoService>()
                .AddSingleton<IDeviceManager, DeviceManager>()
                .AddSingleton<IMdnsService, MdnsService>()
                .AddSingleton<IAdbService, AdbService>()
                .AddSingleton<IScreenMirrorService, ScreenMirrorService>()
                .AddSingleton<IFileTransferService, FileTransferService>()
                .AddSingleton<IClipboardService, ClipboardService>()
                .AddSingleton<IRemoteAppService, RemoteAppService>()
                
                // 2. 注册IMessageHandler，它依赖RemoteAppRepository，而RemoteAppRepository依赖Func<INetworkService>
                .AddSingleton<IMessageHandler, MessageHandler>()
                .AddSingleton<Func<IMessageHandler>>(sp => () => sp.GetRequiredService<IMessageHandler>())
                
                // 3. 注册ProtocolRouter，它依赖IMessageHandler
#if WINDOWS
                // 在Windows平台上，ProtocolRouter需要NetworkDriveMapper
                .AddSingleton<Func<NetworkDriveMapper>>(sp => () => sp.GetRequiredService<NetworkDriveMapper>())
#endif
                .AddSingleton<ProtocolRouter>()
                
                // 4. 注册INetworkService和工厂函数，它依赖ProtocolRouter
                .AddSingleton<INetworkService, NetworkService>()
                .AddSingleton<Func<INetworkService>>(sp => () => sp.GetRequiredService<INetworkService>())
                
                // 5. 注册ISessionManager和ITcpServerProvider，它们由INetworkService实现
                .AddSingleton<ISessionManager>(sp => (ISessionManager)sp.GetRequiredService<INetworkService>())
                .AddSingleton<ITcpServerProvider>(sp => (ITcpServerProvider)sp.GetRequiredService<INetworkService>())
                
                // 6. 注册INotificationService，它依赖ISessionManager
                .AddSingleton<INotificationService, NotificationService>()
                .AddSingleton<Func<INotificationService>>(sp => () => sp.GetRequiredService<INotificationService>())

                // 注册其他需要的工厂
                .AddSingleton<Func<IClipboardService>>(sp => () => sp.GetRequiredService<IClipboardService>())
                .AddSingleton<Func<IRemoteAppService>>(sp => () => sp.GetRequiredService<IRemoteAppService>())
                .AddSingleton<Func<IPlaybackService>>(sp => () => sp.GetRequiredService<IPlaybackService>())
                
                // 7. 注册IDiscoveryService，它依赖INetworkService
                .AddSingleton<IDiscoveryService, DiscoveryService>()

                // ViewModels
                .AddSingleton<MainPageViewModel>()
                .AddSingleton<DevicesViewModel>()
                .AddSingleton<AppsViewModel>()
                )
            );
    }

    /// <summary>
    /// Shows exception on the Debug Output.
    /// </summary>
    public static void HandleAppUnhandledException(Exception? ex)
    {
        Ioc.Default.GetService<ILogger>()?.LogCritical("Unhandled exception {ex}", ex);
    }

    public static async Task HandleStartupTaskAsync(bool enable)
    {
#if WINDOWS
        var startupTask = await StartupTask.GetAsync("8B5D3E3F-9B69-4E8A-A9F7-BFCA793B9AF0");

        if (enable)
        {
            if (startupTask.State == StartupTaskState.Disabled)
                await startupTask.RequestEnableAsync();
        }
        else
        {
            if (startupTask.State == StartupTaskState.Enabled)
                startupTask.Disable();
        }
#endif
    }
}
