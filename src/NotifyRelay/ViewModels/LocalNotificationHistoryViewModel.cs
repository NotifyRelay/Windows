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

    public ObservableCollection<GroupedNotification> GroupedLocalNotifications { get; } = [];

    [ObservableProperty]
    public partial bool IsLoading { get; set; }

    public IAsyncRelayCommand<string?> ClearGroupCommand { get; }
    public IAsyncRelayCommand ClearAllCommand { get; }

    public LocalNotificationHistoryViewModel()
    {
        ClearGroupCommand = new AsyncRelayCommand<string?>(ClearGroupAsync);
        ClearAllCommand = new AsyncRelayCommand(ClearAllAsync);
        _ = LoadLocalNotificationsAsync();
        LocalNotificationListenerService.LocalNotificationCaptured += OnLocalNotificationCaptured;
    }

    private async Task ClearGroupAsync(string? packageName)
    {
        if (string.IsNullOrEmpty(packageName)) return;
        try
        {
            var device = await DeviceManager.GetLocalDeviceAsync();
            if (device == null) return;

            await Task.Run(() => NotificationRepository.ClearDeviceNotificationsByPackage(device.DeviceId, packageName));
            await LoadLocalNotificationsAsync();
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "清除分组 {Package} 失败", packageName);
        }
    }

    private async Task ClearAllAsync()
    {
        try
        {
            var device = await DeviceManager.GetLocalDeviceAsync();
            if (device == null) return;

            await Task.Run(() => NotificationRepository.ClearDeviceNotifications(device.DeviceId));
            await LoadLocalNotificationsAsync();
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "清除全部通知失败");
        }
    }

    private async Task LoadLocalNotificationsAsync()
    {
        await dispatcher.EnqueueAsync(() => IsLoading = true);
        try
        {
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
                    var appPackage = root.TryGetProperty("packageName", out var pn) && pn.ValueKind == JsonValueKind.String ? pn.GetString() : null;
                    if (!string.IsNullOrEmpty(appPackage))
                    {
                        if (!IconUtils.AppIconExists(appPackage))
                        {
                            Logger.LogDebug("LocalHistory: 图标文件不存在，跳过加载: {Package}", appPackage);
                        }
                    }

                    notifications.Add(notif);
                }
                catch (Exception ex)
                {
                    Logger.LogError(ex, "解析本地通知实体失败");
                }
            }

            var sorted = notifications.OrderByDescending(n => n.TimeStamp).ToList();

            var groups = sorted
                .GroupBy(n => n.AppPackage ?? n.AppName ?? "unknown")
                .ToList();

            var groupedList = new List<GroupedNotification>();
            foreach (var group in groups)
            {
                var first = group.First();
                var groupNotif = new GroupedNotification
                {
                    Id = group.Key,
                    AppPackage = group.Key,
                    AppName = first.AppName ?? group.Key,
                    IconPath = first.IconPath
                };
                foreach (var notif in group)
                    groupNotif.AddNotification(notif);
                groupedList.Add(groupNotif);
            }

            groupedList = groupedList.OrderByDescending(g => g.LatestTime).ToList();

            await dispatcher.EnqueueAsync(() =>
            {
                GroupedLocalNotifications.Clear();
                foreach (var g in groupedList)
                    GroupedLocalNotifications.Add(g);
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
