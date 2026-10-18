using System.Collections.Specialized;
using CommunityToolkit.WinUI;
using NotifyRelay.Data.Contracts;
using NotifyRelay.Data.Models;

namespace NotifyRelay.Services.Notifications;

/// <summary>
/// 通知排序与分组服务：按时间重排活动通知集合并重建分组集合（从 NotificationService 提取）。
/// 角标逻辑已移交 INotificationBadgeService。
/// </summary>
public class NotificationGrouper(
    Microsoft.UI.Dispatching.DispatcherQueue dispatcher,
    INotificationBadgeService badgeService,
    IDeviceManager deviceManager) : INotificationGrouper
{
    private readonly ObservableCollection<GroupedNotification> groupedNotifications = [];
    private ReadOnlyObservableCollection<GroupedNotification>? groupedNotificationsReadOnly;

    /// <summary>
    /// 分组通知集合变化事件
    /// </summary>
    public event NotifyCollectionChangedEventHandler? GroupedNotificationsChanged;

    /// <summary>
    /// 触发分组通知变化事件
    /// </summary>
    private void OnGroupedNotificationsChanged(NotifyCollectionChangedEventArgs e) => GroupedNotificationsChanged?.Invoke(this, e);

    /// <summary>
    /// Gets grouped notifications from all devices
    /// </summary>
    public ReadOnlyObservableCollection<GroupedNotification> GroupedNotificationHistory => groupedNotificationsReadOnly ??= new ReadOnlyObservableCollection<GroupedNotification>(groupedNotifications);

    /// <summary>
    /// 按时间重排 activeNotifications 并重建分组集合
    /// </summary>
    public void Rebuild(ObservableCollection<Notification> activeNotifications)
    {
        dispatcher.EnqueueAsync(async () =>
        {
            // 保存现有分组的展开/折叠状态和时间信息
            Dictionary<string, (bool IsCollapsed, DateTime EarliestTime, DateTime LatestTime)> existingGroupStates = [];
            foreach (var existingGroup in groupedNotifications)
            {
                existingGroupStates[existingGroup.Id] = (existingGroup.IsCollapsed, existingGroup.EarliestTime, existingGroup.LatestTime);
            }

            // 排序 activeNotifications
            var sortedNotifications = activeNotifications.OrderByDescending(n => n.TimeStamp).ToList();
            activeNotifications.Clear();
            foreach (var n in sortedNotifications) activeNotifications.Add(n);

            groupedNotifications.Clear();

            // Update badge（修复 F1：改为内部取当前活动设备，保证本次能真正设置角标）
            var activeDevice = deviceManager.ActiveDevice;
            await badgeService.UpdateBadgeAsync(activeNotifications.Count, activeDevice);

            // Group notifications
            Dictionary<string, List<Notification>> appNotificationsDict = [];
            List<Notification> pinnedNotifications = [];

            foreach (var notification in activeNotifications)
            {
                if (notification.Pinned)
                {
                    pinnedNotifications.Add(notification);
                }
                else
                {
                    string groupKey = notification.AppPackage ?? "UnknownApp";
                    if (!appNotificationsDict.TryGetValue(groupKey, out var notificationsList))
                    {
                        notificationsList = [];
                        appNotificationsDict[groupKey] = notificationsList;
                    }
                    notificationsList.Add(notification);
                }
            }

            Dictionary<string, GroupedNotification> groupedNotificationsDict = [];
            List<Notification> singleNotifications = [];

            foreach (var (groupKey, notificationsList) in appNotificationsDict)
            {
                if (notificationsList.Count == 1)
                {
                    singleNotifications.Add(notificationsList[0]);
                }
                else
                {
                    var notificationTime = ParseNotificationTime(notificationsList[0]);
                    var group = new GroupedNotification
                    {
                        Id = groupKey,
                        EarliestTime = notificationTime,
                        LatestTime = notificationTime
                    };

                    if (existingGroupStates.TryGetValue(groupKey, out var groupState))
                    {
                        group.IsCollapsed = groupState.IsCollapsed;
                        if (!groupState.IsCollapsed)
                        {
                            group.EarliestTime = groupState.EarliestTime;
                            group.LatestTime = groupState.LatestTime;
                        }
                    }

                    foreach (var notif in notificationsList)
                    {
                        group.AddNotification(notif);
                    }

                    groupedNotificationsDict[groupKey] = group;
                }
            }

            var finalNotifications = new List<object>();
            finalNotifications.AddRange(pinnedNotifications.OrderByDescending(n => n.TimeStamp));

            var nonPinnedNotifications = new List<object>();
            foreach (var notification in singleNotifications) nonPinnedNotifications.Add(notification);
            nonPinnedNotifications.AddRange(groupedNotificationsDict.Values);

            var sortedNonPinnedNotifications = nonPinnedNotifications.OrderByDescending(item =>
            {
                if (item is Notification notification) return ParseNotificationTime(notification);
                else if (item is GroupedNotification group) return group.LatestTime;
                return DateTime.MinValue;
            }).ToList();

            finalNotifications.AddRange(sortedNonPinnedNotifications);

            foreach (var item in finalNotifications)
            {
                if (item is GroupedNotification group)
                {
                    groupedNotifications.Add(group);
                }
                else if (item is Notification notification)
                {
                    var singleGroup = new GroupedNotification
                    {
                        Id = notification.Key,
                        EarliestTime = ParseNotificationTime(notification)
                    };
                    singleGroup.AddNotification(notification);
                    groupedNotifications.Add(singleGroup);
                }
            }

            OnGroupedNotificationsChanged(new System.Collections.Specialized.NotifyCollectionChangedEventArgs(System.Collections.Specialized.NotifyCollectionChangedAction.Reset));
        });
    }

    private static DateTime ParseNotificationTime(Notification notification)
    {
        if (notification.TimeStamp != null && long.TryParse(notification.TimeStamp, out var timestamp))
        {
            return DateTimeOffset.FromUnixTimeMilliseconds(timestamp).DateTime;
        }
        return DateTime.Now;
    }
}
