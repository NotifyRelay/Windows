using CommunityToolkit.WinUI;
using NotifyRelay.Data.AppDatabase.Repository;
using NotifyRelay.Data.Contracts;
using NotifyRelay.Data.Models;
using NotifyRelay.Services;
using NotifyRelay.Utils;
using NotifyRelay.Utils.Serialization;

namespace NotifyRelay.ViewModels;

public sealed partial class LocalNotificationHistoryViewModel : BaseViewModel
{
    private NotificationRepository NotificationRepository { get; } = Ioc.Default.GetRequiredService<NotificationRepository>();
    private IDeviceManager DeviceManager { get; } = Ioc.Default.GetRequiredService<IDeviceManager>();

    public ObservableCollection<Notification> LocalNotifications { get; } = [];

    [ObservableProperty]
    public partial bool IsLoading { get; set; }

    public LocalNotificationHistoryViewModel()
    {
        _ = LoadLocalNotificationsAsync();
        LocalNotificationListenerService.LocalNotificationCaptured += OnLocalNotificationCaptured;
    }

    private async Task LoadLocalNotificationsAsync()
    {
        try
        {
            IsLoading = true;

            var device = await DeviceManager.GetLocalDeviceAsync();
            if (device == null) return;

            var entities = await Task.Run(() => NotificationRepository.GetDeviceNotifications(device.DeviceId, 200));
            var notifications = new List<Notification>();

            foreach (var entity in entities)
            {
                try
                {
                    var msg = SocketMessageSerializer.DeserializeMessage(entity.MessageJson) as NotificationMessage;
                    if (msg == null) continue;

                    var notif = await Notification.FromMessage(msg);
                    notif.Pinned = entity.Pinned;
                    notif.AddSourceDevice(device.DeviceId, device.DeviceName ?? device.DeviceId);

                    if (!string.IsNullOrEmpty(msg.AppPackage))
                    {
                        string iconPath = IconUtils.GetAppIconPath(msg.AppPackage);
                        notif.IconPath = iconPath;
                        if (IconUtils.AppIconExists(msg.AppPackage))
                            await notif.LoadIconAsync();
                    }

                    notifications.Add(notif);
                }
                catch (Exception ex)
                {
                    Logger.LogError(ex, "解析本地通知实体失败");
                }
            }

            await dispatcher.EnqueueAsync(() =>
            {
                LocalNotifications.Clear();
                foreach (var n in notifications.OrderByDescending(n => n.TimeStamp))
                    LocalNotifications.Add(n);
            });
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "加载本机通知历史失败");
        }
        finally
        {
            await dispatcher.EnqueueAsync(() => IsLoading = false);
        }
    }

    private void OnLocalNotificationCaptured()
    {
        _ = LoadLocalNotificationsAsync();
    }
}
