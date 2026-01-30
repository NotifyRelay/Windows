using NotifyRelay.Data.Contracts;
using NotifyRelay.Platforms.Desktop.Services;

namespace NotifyRelay.Platforms.Desktop;

/// <summary>
/// Extension methods for registering Desktop-specific services
/// </summary>
public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddDesktopServices(this IServiceCollection services)
    {
        services.AddSingleton<IPlatformNotificationHandler, DesktopNotificationHandler>();
        services.AddSingleton<IPlaybackService, DesktopPlaybackService>();
        services.AddSingleton<IActionService, DesktopActionService>();
        services.AddSingleton<IUpdateService, DesktopUpdateService>();
        services.AddSingleton<IftpService, DesktopftpService>();
        return services;
    }
}
