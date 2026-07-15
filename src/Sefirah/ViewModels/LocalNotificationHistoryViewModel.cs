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
                    var msgJson = SocketMessageSerializer.DeserializeMessage(entity.MessageJson);
                    if (msgJson == null) continue;

                    var notif = await Notification.FromMessage(msgJson);
                    notif.Pinned = entity.Pinned;
                    notif.AddSourceDevice(device.DeviceId, device.DeviceName ?? device.DeviceId);

                    using var doc = JsonDocument.Parse(msgJson);
                    var root = doc.RootElement;
                    var appPackage = (root.TryGetProperty("packageName", out var pn) && pn.ValueKind == JsonValueKind.String ? pn.GetString() : null)
                        ?? (root.TryGetProperty("appPackage", out var ap) && ap.ValueKind == JsonValueKind.String ? ap.GetString() : null);
                    if (!string.IsNullOrEmpty(appPackage))
                    {
                        string iconPath = IconUtils.GetAppIconPath(appPackage);
                        notif.IconPath = iconPath;
                        if (IconUtils.AppIconExists(appPackage))
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
