using CommunityToolkit.WinUI;
using Microsoft.UI.Xaml.Media.Imaging;
using Sefirah.Data.Enums;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace Sefirah.Data.Models;

public partial class GroupedNotification : ObservableObject
{
    private bool _isCollapsed = true;
    private bool _isRead = false;
    private ObservableCollection<Notification> _notifications = new ObservableCollection<Notification>();
    private string? _appName;
    private string? _appPackage;
    private string? _iconPath;
    private BitmapImage? _icon;
    private DateTime _earliestTime;
    private DateTime _latestTime;

    public string Id { get; set; } = string.Empty;
    
    public string? AppName
    {
        get => _appName;
        set => SetProperty(ref _appName, value);
    }

    public string? AppPackage
    {
        get => _appPackage;
        set => SetProperty(ref _appPackage, value);
    }

    public bool IsCollapsed
    {
        get => _isCollapsed;
        set => SetProperty(ref _isCollapsed, value);
    }

    public bool IsRead
    {
        get => _isRead;
        set => SetProperty(ref _isRead, value);
    }

    public ObservableCollection<Notification> Notifications
    {
        get => _notifications;
        set => SetProperty(ref _notifications, value);
    }

    public string? IconPath
    {
        get => _iconPath;
        set => SetProperty(ref _iconPath, value);
    }

    public BitmapImage? Icon
    {
        get => _icon;
        set => SetProperty(ref _icon, value);
    }

    public DateTime EarliestTime
    {
        get => _earliestTime;
        set => SetProperty(ref _earliestTime, value);
    }

    public DateTime LatestTime
    {
        get => _latestTime;
        set => SetProperty(ref _latestTime, value);
    }

    public string FormattedLatestTime => LatestTime.ToString("MM-dd HH:mm");

    [ObservableProperty]
    private bool _hasExtra = false;

    [ObservableProperty]
    private string _collapsedExtraText = string.Empty;

    public ObservableCollection<string> Devices
    {
        get
        {
            var devices = new ObservableCollection<string>();
            foreach (var notification in Notifications)
            {
                foreach (var sourceDevice in notification.SourceDevices)
                {
                    if (!devices.Contains(sourceDevice.DeviceName))
                    {
                        devices.Add(sourceDevice.DeviceName);
                    }
                }
            }
            return devices;
        }
    }

    public bool IsSingle => Notifications.Count == 1;

    public ObservableCollection<Notification> CollapsedNotifications
    {
        get
        {
            // 只显示前3条通知
            if (Notifications.Count <= 3)
            {
                return Notifications;
            }
            
            var collapsedList = new ObservableCollection<Notification>();
            for (int i = 0; i < 3; i++)
            {
                collapsedList.Add(Notifications[i]);
            }
            
            HasExtra = true;
            CollapsedExtraText = $"+{Notifications.Count - 3} 更多通知";
            
            return collapsedList;
        }
    }

    public string DisplayApp => AppName ?? AppPackage ?? "未知应用";
    
    public string NotificationCountText => $"{Notifications.Count} 条通知";
    
    public string FormattedEarliestTime => EarliestTime.ToString("MM-dd HH:mm");

    public void ToggleCollapse()
    {
        IsCollapsed = !IsCollapsed;
    }

    public void AddNotification(Notification notification)
    {
        Notifications.Add(notification);
        
        // 更新时间信息
        if (notification.TimeStamp != null)
        {
            var notificationTime = DateTimeOffset.FromUnixTimeMilliseconds(long.Parse(notification.TimeStamp)).DateTime;
            if (notificationTime < EarliestTime)
            {
                EarliestTime = notificationTime;
            }
            if (notificationTime > LatestTime)
            {
                LatestTime = notificationTime;
            }
        }
        
        // 更新应用信息
        if (string.IsNullOrEmpty(AppName))
        {
            AppName = notification.AppName;
            AppPackage = notification.AppPackage;
            IconPath = notification.IconPath;
            Icon = notification.Icon;
        }
        else if (Icon == null && notification.Icon != null)
        {
            // 如果分组没有图标，但通知有图标，更新分组图标
            IconPath = notification.IconPath;
            Icon = notification.Icon;
        }
    }
    
    public static implicit operator Notification(GroupedNotification group)
    {
        // 仅用于单条通知的情况，返回第一条通知
        return group.Notifications.FirstOrDefault() ?? new Notification();
    }
}