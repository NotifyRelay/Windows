using System.Threading.Channels;
using NotifyRelay.Data.Contracts;
using NotifyRelay.Platforms.Windows.RemoteStorage.Abstractions;
using NotifyRelay.Platforms.Windows.RemoteStorage.Configuration;
using NotifyRelay.Platforms.Windows.RemoteStorage.Remote;
using NotifyRelay.Platforms.Windows.RemoteStorage.Shell;
using NotifyRelay.Platforms.Windows.RemoteStorage.Shell.Commands;
using NotifyRelay.Platforms.Windows.RemoteStorage.Shell.Local;
using NotifyRelay.Platforms.Windows.RemoteStorage.Worker;
using NotifyRelay.Platforms.Windows.RemoteStorage.Worker.IO;
using NotifyRelay.Platforms.Windows.Services;
using NotifyRelay.Services.Overlay;

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

        // 注册网络磁盘映射服务
        services.AddSingleton<NetworkDriveMapper>();

        // 注册FTP服务，用于处理网络磁盘映射的移除操作
        services.AddSingleton<IftpService, WindowftpService>();

        // 注册键盘钩子服务
        services.AddSingleton<KeyboardHookService>();

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
            .Configure<IConfiguration>((options, config) =>
            {
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
}


