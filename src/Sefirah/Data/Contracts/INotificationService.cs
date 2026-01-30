using NotifyRelay.Data.Models;

namespace NotifyRelay.Data.Contracts;
public interface INotificationService
{
    /// <summary>
    /// Gets notifications for the currently active device session
    /// </summary>
    ReadOnlyObservableCollection<Notification> NotificationHistory { get; }

    /// <summary>
    /// Gets grouped notifications for the currently active device session
    /// </summary>
    ReadOnlyObservableCollection<GroupedNotification> GroupedNotificationHistory { get; }

    /// <summary>
    /// Initializes the notification service
    /// </summary>  
    void Initialize();

    Task HandleNotificationMessage(PairedDevice device, NotificationMessage notificationMessage);
    Task HandleMediaPlayNotification(PairedDevice device, NotificationMessage notificationMessage);
    void RemoveNotification(PairedDevice device, Notification notification);

    /// <summary>
    /// Toggles pin status for a notification in the active session
    /// </summary>
    void TogglePinNotification(PairedDevice device, Notification notification);

    /// <summary>
    /// Clears all notifications for the specified device
    /// </summary>
    void ClearAllNotification(PairedDevice device);

    /// <summary>
    /// Clears all notifications for all devices
    /// </summary>
    void ClearAllNotificationall();

    /// <summary>
    /// Clears all notifications for a specific app package across all devices
    /// </summary>
    void ClearAllNotifications(string appPackage);

    void ClearHistory(PairedDevice device);
    void HandleIconResponse(string deviceId, string packageName);

    /// <summary>
    /// 当前显示的音乐媒体块列表（只读，支持多个设备同时显示）
    /// </summary>
    ReadOnlyObservableCollection<MusicMediaBlock> CurrentMusicMediaBlocks { get; }

    /// <summary>
    /// 处理音乐媒体块超时
    /// </summary>
    void CheckMusicMediaBlockTimeout();

    /// <summary>
    /// 处理媒体播放消息 (DATA_MEDIAPLAY)
    /// </summary>
    Task ProcessMediaPlayMessageAsync(PairedDevice device, string payload);

    /// <summary>
    /// 处理图标响应消息 (DATA_ICON_RESPONSE)
    /// </summary>
    Task ProcessIconResponseAsync(PairedDevice device, string payload);

    /// <summary>
    /// 处理普通通知消息 (DATA_NOTIFICATION)
    /// </summary>
    Task ProcessNotificationMessageAsync(PairedDevice device, string payload);

    /// <summary>
    /// 分组通知集合变化事件
    /// </summary>
    event System.Collections.Specialized.NotifyCollectionChangedEventHandler GroupedNotificationsChanged;
}
