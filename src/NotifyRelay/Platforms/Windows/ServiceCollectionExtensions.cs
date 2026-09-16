using NotifyRelay.Data.Contracts;
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
        services.AddSingleton<AudioDeviceManager>();
        services.AddSingleton<SmtcSessionRegistry>();
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
}
