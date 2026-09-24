using CommunityToolkit.WinUI;
using NotifyRelay.Data.AppDatabase.Repository;
using NotifyRelay.Data.Contracts;
using NotifyRelay.Data.Enums;
using NotifyRelay.Data.Models;
// LocalNotificationListenerService 与 BaseActionService 保留在 NotifyRelay.Services 根命名空间
using NotifyRelay.Services;
using NotifyRelay.Services.Notifications;
using NotifyRelay.Services.Overlay;
using NotifyRelay.Services.Protocol;
using NotifyRelay.Utils;
using NotifyRelay.Utils.Serialization;
using Windows.System;
using Notification = NotifyRelay.Data.Models.Notification;

namespace NotifyRelay.Services;

public class NotificationService(
    ILogger logger,
    IDeviceManager deviceManager,
    IPlatformNotificationHandler platformNotificationHandler,
    RemoteAppRepository remoteAppsRepository,
    NotificationRepository notificationRepository,
    IPlaybackService playbackService,
    IGeneralSettingsService generalSettings,
    OverlayRenderService overlayRender,
    INotificationGrouper grouper,
    IMusicMediaBlockManager musicMediaBlockManager,
    INotificationIconResolver iconResolver,
    INotificationBadgeService badgeService) : INotificationService, INotifyPropertyChanged
{
    private readonly Microsoft.UI.Dispatching.DispatcherQueue dispatcher = App.MainWindow.DispatcherQueue;

    private readonly ObservableCollection<Notification> activeNotifications = [];

    /// <summary>
    /// 属性变更事件
    /// </summary>
    public event PropertyChangedEventHandler? PropertyChanged;

    protected virtual void OnPropertyChanged(string? propertyName) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

    /// <summary>
    /// 分组通知集合变化事件（转发 NotificationGrouper 的事件）
    /// </summary>
    public event System.Collections.Specialized.NotifyCollectionChangedEventHandler? GroupedNotificationsChanged
    {
        add => grouper.GroupedNotificationsChanged += value;
        remove => grouper.GroupedNotificationsChanged -= value;
    }

    /// <summary>
    /// Gets all notifications from all devices
    /// </summary>
    public ReadOnlyObservableCollection<Notification> NotificationHistory => new(activeNotifications);

    /// <summary>
    /// Gets grouped notifications from all devices
    /// </summary>
    public ReadOnlyObservableCollection<GroupedNotification> GroupedNotificationHistory => grouper.GroupedNotificationHistory;

    /// <summary>
    /// 当前显示的音乐媒体块列表（只读）
    /// </summary>
    public ReadOnlyObservableCollection<MusicMediaBlock> CurrentMusicMediaBlocks => musicMediaBlockManager.Blocks;

    // Initialize the service - call this after DI container creates the instance
    public void Initialize()
    {
        // 注入通知集合访问器与重建回调，避免 Resolver 反向依赖本服务
        iconResolver.Configure(
            notificationsProvider: () => activeNotifications,
            rebuildCallback: () => grouper.Rebuild(activeNotifications));

        _ = badgeService.ClearBadgeAsync(); // 显式丢弃：异常已在 BadgeService 内记录

        // Load all notifications at startup
        _ = LoadAllNotificationsAsync();

        // 初始化音乐媒体块超时检查定时器，每1秒检查一次
        musicMediaBlockManager.StartTimeoutChecker();

        // 订阅 Socket 指令
        LocalSocketRelayServer.CommandReceived += OnSocketCommandReceived;
    }

    private async void OnSocketCommandReceived(object? sender, string commandJson)
    {
        try
        {
            using var doc = JsonDocument.Parse(commandJson);
            var root = doc.RootElement;
            if (root.TryGetProperty("action", out var actionProp) && actionProp.GetString() == "media_control")
            {
                var command = root.TryGetProperty("command", out var commandProp) ? commandProp.GetString() : null;
                if (!string.IsNullOrEmpty(command))
                {
                    var actionTypeStr = command switch
                    {
                        "playPause" => "Play",
                        "next" => "Next",
                        "previous" => "Previous",
                        _ => "Play"
                    };
                    var actionJson = JsonSerializer.Serialize(new
                    {
                        playbackActionType = actionTypeStr,
                        source = "MediaControl"
                    });
                    await playbackService.HandleMediaActionAsync(actionJson);
                }
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "处理Socket指令失败");
        }
    }





    public async Task HandleNotificationMessage(PairedDevice device, string payload)
    {
        using var doc = JsonDocument.Parse(payload);
        var root = doc.RootElement;

        var notificationType = root.TryGetProperty("notificationType", out var ntProp) && ntProp.ValueKind == JsonValueKind.String
            ? Enum.TryParse<NotificationType>(ntProp.GetString(), true, out var nt) ? nt : NotificationType.New
            : NotificationType.New;
        // 通知入站字段归一化委托 core（packageName/appName/title/text/time）
        var parsedJson = SuperIslandProtocol.ParseNotificationInbound(payload);
        string? title = null;
        string? appPackage = null;
        string? appName = null;
        string? text = null;
        string timeStamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString();
        if (!string.IsNullOrEmpty(parsedJson))
        {
            try
            {
                using var parsedDoc = JsonDocument.Parse(parsedJson);
                var parsed = parsedDoc.RootElement;
                title = parsed.TryGetProperty("title", out var tProp) ? tProp.GetString() : null;
                appPackage = parsed.TryGetProperty("packageName", out var pnProp) ? pnProp.GetString() : null;
                appName = parsed.TryGetProperty("appName", out var anProp) ? anProp.GetString() : null;
                text = parsed.TryGetProperty("text", out var txProp) ? txProp.GetString() : null;
                if (parsed.TryGetProperty("time", out var tsProp) && tsProp.ValueKind == JsonValueKind.Number)
                {
                    var timeVal = tsProp.GetInt64();
                    if (timeVal != 0) timeStamp = timeVal.ToString();
                }
            }
            catch { /* core 归一解析失败，使用缺省值 */ }
        }
        var notificationKey = root.TryGetProperty("notificationKey", out var nkProp) && nkProp.ValueKind == JsonValueKind.String ? nkProp.GetString() ?? Guid.NewGuid().ToString() : Guid.NewGuid().ToString();
        var appIcon = root.TryGetProperty("appIcon", out var aiProp) ? aiProp.GetString() : null;
        var isLocked = root.TryGetProperty("isLocked", out var ilProp) && ilProp.GetBoolean();
        var bigPicture = root.TryGetProperty("bigPicture", out var bpProp) ? bpProp.GetString() : null;
        var largeIcon = root.TryGetProperty("largeIcon", out var liProp) ? liProp.GetString() : null;
        var coverUrl = root.TryGetProperty("coverUrl", out var cuProp) ? cuProp.GetString() : null;
        var mediaType = root.TryGetProperty("mediaType", out var mtProp) ? mtProp.GetString() : null;
        var tag = root.TryGetProperty("tag", out var tgProp) ? tgProp.GetString() : null;
        var groupKey = root.TryGetProperty("groupKey", out var gkProp) ? gkProp.GetString() : null;

        logger.LogDebug("收到通知消息: NotificationType={NotificationType}, Title={Title}, AppPackage={AppPackage}, AppName={AppName}, Text={Text}",
            notificationType, title, appPackage, appName, text);

        // Check if device has notification sync enabled
        if (!device.DeviceSettings.NotificationSyncEnabled)
        {
            logger.LogDebug("设备通知同步已禁用，跳过通知");
            return;
        }

        try
        {
            // 过滤超级岛通知，识别段是'superisland:'
            if (appPackage?.StartsWith("superisland:") == true)
            {
                return;
            }

            if (notificationType == NotificationType.Removed)
            {
                await dispatcher.EnqueueAsync(() =>
                {
                    var notification = activeNotifications.FirstOrDefault(n =>
                        n.Key == notificationKey ||
                        (n.AppPackage == appPackage &&
                         n.Title == title &&
                         n.Text == text));

                    if (notification != null && !notification.Pinned)
                    {
                        var source = notification.SourceDevices.FirstOrDefault(sd => sd.DeviceId == device.Id);
                        if (source != null)
                        {
                            notification.SourceDevices.Remove(source);
                        }

                        if (notification.SourceDevices.Count == 0)
                        {
                            activeNotifications.Remove(notification);
                        }

                        notificationRepository.DeleteNotification(device.Id, notificationKey);
                        grouper.Rebuild(activeNotifications);
                    }
                });
                return;
            }

            if (title is not null && appPackage is not null)
            {
                var filter = remoteAppsRepository.GetAppNotificationFilterAsync(appPackage, device.Id)
                ?? await remoteAppsRepository.AddOrUpdateApplicationForDevice(device.Id, appPackage, appName, appIcon);

                if (filter == NotificationFilter.Disabled) return;

                await iconResolver.WaitForIconAsync(device.Id, appPackage);

                await dispatcher.EnqueueAsync(async () =>
                        {
                            var existingNotification = activeNotifications.FirstOrDefault(n =>
                                n.AppPackage == appPackage &&
                                n.Title == title &&
                                n.Text == text &&
                                n.Type == notificationType);

                            bool isNewToUser = existingNotification is null;
                            Notification notification;

                            if (existingNotification != null)
                            {
                                notification = existingNotification;
                                if (!notification.SourceDevices.Any(sd => sd.DeviceId == device.Id))
                                {
                                    notification.AddSourceDevice(device.Id, device.Name);
                                }

                                if (notification.Icon == null && !string.IsNullOrEmpty(appPackage))
                                {
                                    notification.IconPath = IconUtils.GetAppIconPath(appPackage);
                                    if (IconUtils.AppIconExists(appPackage)) await notification.LoadIconAsync();
                                }
                            }
                            else
                            {
                                notification = await Notification.FromMessage(payload);
                                notification.AddSourceDevice(device.Id, device.Name);
                                if (!string.IsNullOrEmpty(appPackage))
                                {
                                    notification.IconPath = IconUtils.GetAppIconPath(appPackage);
                                    if (IconUtils.AppIconExists(appPackage)) await notification.LoadIconAsync();
                                }
                                activeNotifications.Add(notification);
                            }

                            bool shouldSave = true;
                            if (notificationType != NotificationType.New && filter != NotificationFilter.ToastFeed && filter != NotificationFilter.Feed)
                            {
                                shouldSave = false;
                            }

                            if (shouldSave)
                            {
                                notificationRepository.UpsertNotification(device.Id, payload, notification.Pinned);
                            }

                            grouper.Rebuild(activeNotifications);

                            bool isNotifyRelaySelf = appPackage?.Contains("notifyrelay", StringComparison.OrdinalIgnoreCase) == true
                                || appName?.Contains("notifyrelay", StringComparison.OrdinalIgnoreCase) == true;
                            if (device.DeviceSettings.IgnoreWindowsApps && !isNotifyRelaySelf && await IsAppActiveAsync(appName ?? "")) return;

                            if (isNewToUser && notificationType == NotificationType.New)
                            {
                                var forceGamebar = generalSettings.GamebarRelayEnabled;
                                var overlayEnabled = generalSettings.DanmakuNotificationEnabled;

                                // 一次性读取图标文件，同时取得弹幕用原始字节与 TCP 转发用 data URL
                                var (iconBytes, iconUrlForTcp) = await iconResolver.LoadIconAsync(appPackage ?? string.Empty);

                                // Priority chain: Overlay → Gamebar TCP → System notification
                                if (overlayEnabled)
                                {
                                    overlayRender.ShowDanmaku(appName ?? "", title ?? "", text ?? string.Empty, iconBytes, device.Name);
                                }

                                if (forceGamebar || !overlayEnabled)
                                {
                                    bool tcpSent = await LocalSocketRelayServer.SendNotificationAsync(
                                        appName ?? "",
                                        appPackage ?? "",
                                        title ?? "",
                                        text ?? string.Empty,
                                        iconUrlForTcp,
                                        device.Name);

                                    if (!tcpSent && !overlayEnabled)
                                    {
                                        await platformNotificationHandler.ShowRemoteNotification(payload, device.Id);
                                    }
                                }
                            }
                        });
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "处理通知消息时出错");
        }
    }

    public void RemoveNotification(PairedDevice device, Notification notification)
    {
        try
        {
            if (!notification.Pinned)
            {
                _ = dispatcher.EnqueueAsync(() =>
                {
                    // Remove from activeNotifications
                    if (activeNotifications.Contains(notification))
                    {
                        activeNotifications.Remove(notification);
                    }
                    else
                    {
                        var match = activeNotifications.FirstOrDefault(n => n.Key == notification.Key);
                        if (match != null) activeNotifications.Remove(match);
                    }

                    // Remove from DB for all source devices
                    foreach (var source in notification.SourceDevices)
                    {
                        notificationRepository.DeleteNotification(source.DeviceId, notification.Key);
                    }

                    // Also try to delete for the passed device if not in source (just in case)
                    notificationRepository.DeleteNotification(device.Id, notification.Key);

                    platformNotificationHandler.RemoveNotificationsByTagAndGroup(notification.Tag, notification.GroupKey);

                    grouper.Rebuild(activeNotifications);
                });
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "移除通知时出错");
        }
    }

    public void TogglePinNotification(PairedDevice device, Notification notification)
    {
        _ = dispatcher.EnqueueAsync(() =>
    {
        notification.Pinned = !notification.Pinned;

        // Update in DB for all source devices
        foreach (var source in notification.SourceDevices)
        {
            notificationRepository.UpdatePinned(source.DeviceId, notification.Key, notification.Pinned);
        }

        grouper.Rebuild(activeNotifications);
    });
    }

    public void ClearAllNotification(PairedDevice device)
    {
        try
        {
            ClearHistory(device);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "清除全部通知时出错");
        }
    }

    /// <summary>
    /// 清除所有设备的全部通知
    /// </summary>
    public void ClearAllNotificationall()
    {
        _ = dispatcher.EnqueueAsync(() =>
        {
            try
            {
                // Remove all non-pinned from activeNotifications
                var toRemove = activeNotifications.Where(n => !n.Pinned).ToList();
                foreach (var n in toRemove)
                {
                    activeNotifications.Remove(n);
                }

                // Clear DB for all devices
                foreach (var device in deviceManager.PairedDevices)
                {
                    notificationRepository.ClearDeviceNotificationsExceptPinned(device.Id);
                }

                _ = badgeService.ClearBadgeAsync(); // 显式丢弃：异常已在 BadgeService 内记录

                grouper.Rebuild(activeNotifications);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "清除所有设备通知时出错");
            }
        });
    }

    /// <summary>
    /// 按包名清除所有设备上的通知
    /// </summary>
    public void ClearAllNotifications(string appPackage)
    {
        _ = dispatcher.EnqueueAsync(() =>
        {
            try
            {
                var toRemove = activeNotifications.Where(n => !n.Pinned && n.AppPackage == appPackage).ToList();
                foreach (var n in toRemove)
                {
                    activeNotifications.Remove(n);
                    // Delete from DB for all sources
                    foreach (var source in n.SourceDevices)
                    {
                        notificationRepository.DeleteNotification(source.DeviceId, n.Key);
                    }
                }

                grouper.Rebuild(activeNotifications);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "按包名清除通知时出错，包名：{AppPackage}", appPackage);
            }
        });
    }

    public void ClearHistory(PairedDevice device)
    {
        _ = dispatcher.EnqueueAsync(() =>
        {
            try
            {
                // Iterate activeNotifications (backwards or copy)
                for (int i = activeNotifications.Count - 1; i >= 0; i--)
                {
                    var n = activeNotifications[i];
                    if (n.Pinned) continue;

                    var source = n.SourceDevices.FirstOrDefault(sd => sd.DeviceId == device.Id);
                    if (source != null)
                    {
                        n.SourceDevices.Remove(source);
                        if (n.SourceDevices.Count == 0)
                        {
                            activeNotifications.RemoveAt(i);
                        }
                    }
                }

                _ = badgeService.ClearBadgeAsync(); // 显式丢弃：异常已在 BadgeService 内记录
                notificationRepository.ClearDeviceNotificationsExceptPinned(device.Id);

                grouper.Rebuild(activeNotifications);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "清除通知历史时出错");
            }
        });
    }



    public async Task LoadAllNotificationsAsync()
    {
        try
        {
            var allNotifications = new List<Notification>();

            // Gather all notifications from all devices
            foreach (var device in deviceManager.PairedDevices)
            {
                var stored = await Task.Run(() => notificationRepository.GetDeviceNotifications(device.Id));
                foreach (var entity in stored)
                {
                    if (string.IsNullOrEmpty(entity.MessageJson)) continue;

                    var notif = await Notification.FromMessage(entity.MessageJson);
                    notif.Pinned = entity.Pinned;

                    try
                    {
                        var deviceIds = JsonSerializer.Deserialize<List<string>>(entity.DeviceIds) ?? [];
                        var deviceNames = JsonSerializer.Deserialize<List<string>>(entity.DeviceNames) ?? [];

                        for (int i = 0; i < deviceIds.Count; i++)
                        {
                            var deviceId = deviceIds[i];
                            var storedDeviceName = i < deviceNames.Count ? deviceNames[i] : deviceId;
                            var pairedDevice = deviceManager.FindDeviceById(deviceId);
                            var deviceName = pairedDevice?.Name ?? storedDeviceName;

                            notif.AddSourceDevice(deviceId, deviceName);
                        }
                    }
                    catch (Exception ex)
                    {
                        logger.LogError(ex, "解析通知 {Id} 的设备信息失败", entity.Id);
                        notif.AddSourceDevice(device.Id, device.Name);
                    }

                    using var msgDoc = JsonDocument.Parse(entity.MessageJson);
                    var msgRoot = msgDoc.RootElement;
                    var msgAppPackage = msgRoot.TryGetProperty("packageName", out var mPn) && mPn.ValueKind == JsonValueKind.String ? mPn.GetString() : null;
                    if (!string.IsNullOrEmpty(msgAppPackage))
                    {
                        string iconPath = IconUtils.GetAppIconPath(msgAppPackage);
                        notif.IconPath = iconPath;
                        if (IconUtils.AppIconExists(msgAppPackage))
                        {
                            await notif.LoadIconAsync();
                        }
                    }

                    allNotifications.Add(notif);
                }
            }

            // Aggregate
            var aggregated = new Dictionary<string, Notification>();
            foreach (var n in allNotifications)
            {
                string key = $"{n.AppPackage}|{n.Title}|{n.Text}|{n.Type}";
                if (aggregated.TryGetValue(key, out var existing))
                {
                    foreach (var sd in n.SourceDevices) existing.AddSourceDevice(sd.DeviceId, sd.DeviceName);

                    if (existing.Icon == null && n.Icon != null) existing.Icon = n.Icon;
                    if (string.IsNullOrEmpty(existing.IconPath) && !string.IsNullOrEmpty(n.IconPath)) existing.IconPath = n.IconPath;
                }
                else
                {
                    aggregated[key] = n;
                }
            }

            foreach (var n in aggregated.Values)
            {
                if (n.Icon == null && !string.IsNullOrEmpty(n.AppPackage) && IconUtils.AppIconExists(n.AppPackage))
                {
                    await n.LoadIconAsync();
                }
            }

            await dispatcher.EnqueueAsync(() =>
            {
                activeNotifications.Clear();
                foreach (var n in aggregated.Values.OrderByDescending(x => x.TimeStamp))
                {
                    activeNotifications.Add(n);
                }
                grouper.Rebuild(activeNotifications);
            });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to load all notifications");
        }
    }

    private async Task<bool> IsAppActiveAsync(string appName)
    {
        try
        {
            // Get all running apps
            var diagnosticInfo = await AppDiagnosticInfo.RequestInfoAsync();
            var isAppActive = diagnosticInfo.Any(info =>
                info.AppInfo.DisplayInfo.DisplayName.Equals(appName, StringComparison.OrdinalIgnoreCase));
            return isAppActive;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "检查应用 '{AppName}' 是否处于活动状态时出错", appName);
            return false;
        }
    }

    public async Task ProcessNotificationMessageAsync(PairedDevice device, string payload)
    {
        try
        {
            if (!payload.TrimStart().StartsWith('{') && !payload.TrimStart().StartsWith('['))
            {
                logger.LogWarning("跳过非 JSON 通知载荷：{payload}", payload.Length > 50 ? payload[..50] + "..." : payload);
                return;
            }

            logger.LogDebug("处理普通通知消息");
            await HandleNotificationMessage(device, payload);
        }
        catch (JsonException ex)
        {
            logger.LogWarning("解析通知JSON时出错：{ex.Message}", ex.Message);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "处理普通通知消息时出错");
        }
    }

}
