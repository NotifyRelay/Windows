using System.Threading.Channels;
using Microsoft.Extensions.Configuration;
using Renci.SshNet;
using NotifyRelay.Data.Contracts;
using NotifyRelay.Platforms.Windows.RemoteStorage.Abstractions;
using NotifyRelay.Platforms.Windows.RemoteStorage.Configuration;
using NotifyRelay.Platforms.Windows.RemoteStorage.Remote;
using NotifyRelay.Platforms.Windows.RemoteStorage.RemoteAbstractions;
using NotifyRelay.Platforms.Windows.RemoteStorage.Sftp;
using NotifyRelay.Platforms.Windows.RemoteStorage.Shell;
using NotifyRelay.Platforms.Windows.RemoteStorage.Shell.Commands;
using NotifyRelay.Platforms.Windows.RemoteStorage.Shell.Local;
using NotifyRelay.Platforms.Windows.RemoteStorage.Worker;
using NotifyRelay.Platforms.Windows.RemoteStorage.Worker.IO;
using NotifyRelay.Platforms.Windows.Services;

namespace NotifyRelay.Platforms.Windows;

/// <summary>
/// Extension methods for registering Windows-specific services
/// </summary>
public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddWindowsServices(this IServiceCollection services)
    {
        services.AddSingleton<IPlatformNotificationHandler, WindowsNotificationHandler>();
        services.AddSingleton<IPlaybackService, WindowsPlaybackService>();
        services.AddSingleton<IActionService, WindowsActionService>();
        services.AddSingleton<IUpdateService, WindowsUpdateService>();

        // Remote Storage
        services.AddSftpRemoteServices();
        services.AddCloudSyncWorker();

        // Shell
        services.AddCommonClassObjects();
        services.AddSingleton<ShellRegistrar>();
        services.AddHostedService<ShellWorker>();

        services.AddSingleton<SyncProviderWorker>();
        services.AddSingleton<ISftpService, WindowsSftpService>();
        return services;
    }

    public static IServiceCollection AddRemoteFactories(this IServiceCollection services) =>
    services
        .AddScoped<RemoteReadServiceFactory>()
        .AddScoped((sp) => sp.GetRequiredService<RemoteReadServiceFactory>().Create())
        .AddScoped<RemoteReadWriteServiceFactory>()
        .AddScoped((sp) => sp.GetRequiredService<RemoteReadWriteServiceFactory>().Create())
        .AddScoped<RemoteWatcherFactory>()
        .AddScoped((sp) => sp.GetRequiredService<RemoteWatcherFactory>().Create());

    public static IServiceCollection AddClassObject<T>(this IServiceCollection services) where T : class =>
    services
        .AddTransient<T>()
        .AddSingleton<ClassFactory<T>.Generator>((sp) => () => sp.GetRequiredService<T>())
        .AddSingleton<IClassFactoryOf, ClassFactory<T>>();

    public static IServiceCollection AddCommonClassObjects(this IServiceCollection services) =>
        services
            .AddClassObject<SyncCommand>()
            .AddClassObject<UploadCommand>();

    public static IServiceCollection AddLocalClassObjects(this IServiceCollection services) =>
        services
            .AddClassObject<LocalThumbnailProvider>()
            .AddTransient<LocalStatusUiSource>()
            .AddSingleton<CreateStatusUiSource<LocalStatusUiSource>>((sp) => (syncRootId) => sp.GetRequiredService<LocalStatusUiSource>())
            .AddClassObject<LocalStatusUiSourceFactory>();

    public static IServiceCollection AddCloudSyncWorker(this IServiceCollection services) =>
        services
            .AddOptionsWithValidateOnStart<ProviderOptions>()
            .Configure<IConfiguration>((options, config) => {
                options.ProviderId = "Shrimqy:Sefirah";
            })
            .Services
            .AddSingleton<SyncProviderPool>()
            .AddSingleton<SyncProviderContextAccessor>()
            .AddSingleton<ISyncProviderContextAccessor>((sp) => sp.GetRequiredService<SyncProviderContextAccessor>())

            .AddSingleton((sp) =>
                Channel.CreateUnbounded<ShellCommand>(
                    new UnboundedChannelOptions
                    {
                        SingleReader = false,
                    }
                )
            )
        .AddSingleton((sp) => sp.GetRequiredService<Channel<ShellCommand>>().Reader)
        .AddSingleton((sp) => sp.GetRequiredService<Channel<ShellCommand>>().Writer)
        .AddScoped<ShellCommandQueue>()

            // Sync Provider services
            .AddRemoteFactories()
            .AddScoped<FileLocker>()
            .AddScoped((sp) =>
                Channel.CreateUnbounded<Func<Task>>(
                    new UnboundedChannelOptions
                    {
                        SingleReader = true,
                    }
                )
            )
            .AddScoped((sp) => sp.GetRequiredService<Channel<Func<Task>>>().Reader)
            .AddScoped((sp) => sp.GetRequiredService<Channel<Func<Task>>>().Writer)
            .AddScoped<TaskQueue>()
            .AddScoped<SyncProvider>()
            .AddScoped<SyncRootConnector>()
            .AddScoped<SyncRootRegistrar>()
            .AddScoped<PlaceholdersService>()
            .AddScoped<ClientWatcher>()
            .AddScoped<RemoteWatcher>();

    public static IServiceCollection AddSftpRemoteServices(this IServiceCollection services) =>
        services
            // 将SftpContextAccessor改为Scoped，确保每个设备有自己的上下文
            .AddScoped<SftpContextAccessor>()
            // 将SftpContextAccessor注册为IRemoteContextSetter，同时作为Keyed服务和普通服务
            .AddKeyedScoped<IRemoteContextSetter>("sftp", (sp, key) => sp.GetRequiredService<SftpContextAccessor>())
            // 同时注册为普通的Scoped服务，以便SyncProviderPool能够通过GetServices获取
            .AddScoped<IRemoteContextSetter>(sp => sp.GetRequiredService<SftpContextAccessor>())
            // 将ISftpContextAccessor改为Scoped，每个设备有自己的上下文访问器
            .AddScoped<ISftpContextAccessor>((sp) => sp.GetRequiredService<SftpContextAccessor>())
            // 按设备创建SFTP客户端，每个设备有自己的客户端实例
            .AddScoped((sp) => {
                var contextAccessor = sp.GetRequiredService<ISftpContextAccessor>();
                var logger = sp.GetRequiredService<ILogger>();
                
                logger.LogDebug("正在创建SFTP客户端：主机={Host}, 端口={Port}, 用户名={Username}", 
                    contextAccessor.Context.Host, 
                    contextAccessor.Context.Port, 
                    contextAccessor.Context.Username);
                    
                var client = new SftpClient(
                    contextAccessor.Context.Host,
                    contextAccessor.Context.Port,
                    contextAccessor.Context.Username,
                    contextAccessor.Context.Password
                );
                
                try
                {
                    logger.LogDebug("正在连接SFTP服务器：{Host}:{Port}", 
                        contextAccessor.Context.Host, 
                        contextAccessor.Context.Port);
                        
                    client.Connect();
                    logger.LogDebug("SFTP连接成功：{Host}:{Port}", 
                        contextAccessor.Context.Host, 
                        contextAccessor.Context.Port);
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "SFTP连接失败：{Host}:{Port}", 
                        contextAccessor.Context.Host, 
                        contextAccessor.Context.Port);
                }
                
                return client;
            })
            // SFTP相关服务都注册为Scoped，确保每个设备有自己的实例
            .AddKeyedScoped<IRemoteReadWriteService, SftpReadWriteService>("sftp")
            .AddScoped((sp) => new LazyRemote<IRemoteReadWriteService>(() => sp.GetRequiredKeyedService<IRemoteReadWriteService>("sftp"), SftpConstants.KIND))
            .AddKeyedScoped<IRemoteReadService>("sftp", (sp, key) => sp.GetRequiredService<IRemoteReadWriteService>())
            .AddScoped((sp) => new LazyRemote<IRemoteReadService>(() => sp.GetRequiredKeyedService<IRemoteReadService>("sftp"), SftpConstants.KIND))
            .AddKeyedScoped<IRemoteWatcher, SftpWatcher>("sftp")
            .AddScoped((sp) => new LazyRemote<IRemoteWatcher>(() => sp.GetRequiredKeyedService<IRemoteWatcher>("sftp"), SftpConstants.KIND));
} 
