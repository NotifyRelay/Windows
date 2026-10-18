using System.Collections.Specialized;
using NotifyRelay.Data.Models;

namespace NotifyRelay.Data.Contracts;

/// <summary>
/// 通知排序与分组服务：按时间重排活动通知集合并重建分组集合。
/// </summary>
public interface INotificationGrouper
{
    /// <summary>
    /// 分组后的通知集合（只读）
    /// </summary>
    ReadOnlyObservableCollection<GroupedNotification> GroupedNotificationHistory { get; }

    /// <summary>
    /// 分组通知集合变化事件
    /// </summary>
    event NotifyCollectionChangedEventHandler? GroupedNotificationsChanged;

    /// <summary>
    /// 按时间重排 activeNotifications 并重建分组集合
    /// </summary>
    void Rebuild(ObservableCollection<Notification> activeNotifications);
}
