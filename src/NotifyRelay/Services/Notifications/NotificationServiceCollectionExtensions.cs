using NotifyRelay.Data.Contracts;

namespace NotifyRelay.Services.Notifications;

/// <summary>
/// 通知相关服务的注册扩展：把通知子服务与聚合门面的注册收敛在一处，
/// 主注册链只需调用 <see cref="AddNotificationServices"/> 一行。
/// </summary>
public static class NotificationServiceCollectionExtensions
{
    /// <summary>
    /// 注册通知相关的全部服务（角标 / 分组 / 音乐媒体块 / 图标解析 / 聚合门面及其工厂）。
    /// </summary>
    public static IServiceCollection AddNotificationServices(this IServiceCollection services)
    {
        // 子服务共用的 UI 线程调度器
        services.AddSingleton(sp => App.MainWindow.DispatcherQueue);

        // 通知子服务：NotificationGrouper 依赖 INotificationBadgeService
        services.AddSingleton<INotificationBadgeService, NotificationBadgeService>();
        services.AddSingleton<INotificationGrouper, NotificationGrouper>();
        services.AddSingleton<IMusicMediaBlockManager, MusicMediaBlockManager>();
        services.AddSingleton<INotificationIconResolver, NotificationIconResolver>();

        // 聚合门面与工厂（工厂用于打破 ProtocolRouter 的构造期循环依赖）
        services.AddSingleton<INotificationService, NotificationService>();
        services.AddSingleton<Func<INotificationService>>(sp => () => sp.GetRequiredService<INotificationService>());
        services.AddSingleton<Func<IMusicMediaBlockManager>>(sp => () => sp.GetRequiredService<IMusicMediaBlockManager>());
        services.AddSingleton<Func<INotificationIconResolver>>(sp => () => sp.GetRequiredService<INotificationIconResolver>());

        return services;
    }
}
