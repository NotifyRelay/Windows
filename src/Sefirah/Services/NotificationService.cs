using CommunityToolkit.WinUI;
using NotifyRelay.Data.AppDatabase.Repository;
using NotifyRelay.Data.Contracts;
using NotifyRelay.Data.Enums;
using NotifyRelay.Data.Models;
using NotifyRelay.Native;
using NotifyRelay.Services.Filters;
using NotifyRelay.Utils;
using NotifyRelay.Utils.Serialization;
using Windows.Data.Xml.Dom;
using Windows.System;
using Windows.UI.Notifications;
using Notification = NotifyRelay.Data.Models.Notification;

using NotifyRelay.Services.Overlay;

namespace NotifyRelay.Services;

public class NotificationService(
    ILogger logger,
    ISessionManager _sessionManager,
    IDeviceManager deviceManager,
    IPlatformNotificationHandler platformNotificationHandler,
    RemoteAppRepository remoteAppsRepository,
    NotificationRepository notificationRepository,
    Func<INetworkService> _networkServiceFactory,
    Func<IRemoteAppService> remoteAppServiceFactory,
    IPlaybackService playbackService,
    BackendRemoteFilter remoteFilter,
    IGeneralSettingsService generalSettings,
    OverlayRenderService overlayRender) : INotificationService, INotifyPropertyChanged
{
    private readonly Microsoft.UI.Dispatching.DispatcherQueue dispatcher = App.MainWindow.DispatcherQueue;

    private readonly ObservableCollection<Notification> activeNotifications = [];
    private readonly ObservableCollection<GroupedNotification> groupedNotifications = [];
    private readonly object activeNotificationsLock = new(); // 添加锁用于保护 activeNotifications

    // 音乐媒体块相关（支持多个设备同时显示）
    private readonly ObservableCollection<MusicMediaBlock> _currentMusicMediaBlocks = new();
    private ReadOnlyObservableCollection<MusicMediaBlock>? _currentMusicMediaBlocksReadOnly;
    private System.Threading.Timer? _musicMediaBlockTimer;
    private const int MUSIC_MEDIA_BLOCK_TIMEOUT = 30; // 30秒超时

    // 跟踪图标请求状态的字典，key: packageName|deviceId, value: TaskCompletionSource<bool>
    private readonly Dictionary<string, TaskCompletionSource<bool>> pendingIconRequests = [];
    private const int ICON_REQUEST_TIMEOUT = 3000; // 图标请求最长等待时间：3秒

    /// <summary>
    /// 属性变更事件
    /// </summary>
    public event PropertyChangedEventHandler? PropertyChanged;

    protected virtual void OnPropertyChanged(string? propertyName) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

    /// <summary>
    /// 分组通知集合变化事件
    /// </summary>
    public event System.Collections.Specialized.NotifyCollectionChangedEventHandler? GroupedNotificationsChanged;

    /// <summary>
    /// 触发分组通知变化事件
    /// </summary>
    private void OnGroupedNotificationsChanged(System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
    {
        GroupedNotificationsChanged?.Invoke(this, e);
    }

    /// <summary>
    /// Gets all notifications from all devices
    /// </summary>
    public ReadOnlyObservableCollection<Notification> NotificationHistory => new(activeNotifications);

    /// <summary>
    /// Gets grouped notifications from all devices
    /// </summary>
    public ReadOnlyObservableCollection<GroupedNotification> GroupedNotificationHistory => new(groupedNotifications);

    /// <summary>
    /// 当前显示的音乐媒体块列表（只读）
    /// </summary>
    public ReadOnlyObservableCollection<MusicMediaBlock> CurrentMusicMediaBlocks => _currentMusicMediaBlocksReadOnly ??= new ReadOnlyObservableCollection<MusicMediaBlock>(_currentMusicMediaBlocks);

    // Initialize the service - call this after DI container creates the instance
    public void Initialize()
    {
        ClearBadge();

        // Load all notifications at startup
        _ = LoadAllNotificationsAsync();

        // 初始化音乐媒体块超时检查定时器，每1秒检查一次
        _musicMediaBlockTimer = new System.Threading.Timer(
            _ => CheckMusicMediaBlockTimeout(),
            null,
            TimeSpan.FromSeconds(1),
            TimeSpan.FromSeconds(1));

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
        var title = root.TryGetProperty("title", out var tProp) ? tProp.GetString() : null;
        var appPackage = root.TryGetProperty("packageName", out var pnProp) && pnProp.ValueKind == JsonValueKind.String ? pnProp.GetString() : null;
        var appName = root.TryGetProperty("appName", out var anProp) ? anProp.GetString() : null;
        var text = root.TryGetProperty("text", out var txProp) ? txProp.GetString() : null;
        var notificationKey = root.TryGetProperty("notificationKey", out var nkProp) && nkProp.ValueKind == JsonValueKind.String ? nkProp.GetString() : Guid.NewGuid().ToString();
        var timeStamp = root.TryGetProperty("time", out var tsProp) && tsProp.ValueKind == JsonValueKind.Number ? tsProp.GetInt64().ToString() : DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString();
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

            // 应用远程过滤（黑/白名单、包名等价组映射、文本去重）
            if (remoteFilter.ShouldBlock(notificationType, title, appPackage, appName, text))
            {
                logger.LogDebug("远程过滤阻止了通知: {Title}", title);
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
                        UpdateActiveNotifications();
                    }
                });
                return;
            }

            if (title is not null && appPackage is not null)
            {
                var filter = remoteAppsRepository.GetAppNotificationFilterAsync(appPackage, device.Id)
                ?? await remoteAppsRepository.AddOrUpdateApplicationForDevice(device.Id, appPackage, appName, appIcon);

                if (filter == NotificationFilter.Disabled) return;

                bool needIconRequest = !string.IsNullOrEmpty(appPackage) && !IconUtils.AppIconExists(appPackage);
                TaskCompletionSource<bool>? iconRequestTcs = null;
                string? requestKey = null;

                if (needIconRequest)
                {
                    requestKey = $"{appPackage}|{device.Id}";
                    iconRequestTcs = new TaskCompletionSource<bool>();
                    pendingIconRequests[requestKey] = iconRequestTcs;
                    remoteAppServiceFactory().SendIconRequest(device.Id, [appPackage]);
                }

                if (iconRequestTcs != null)
                {
                    var timeoutTask = Task.Delay(ICON_REQUEST_TIMEOUT);
                    var completedTask = await Task.WhenAny(iconRequestTcs.Task, timeoutTask);
                    if (requestKey != null) pendingIconRequests.Remove(requestKey);
                }

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

                            UpdateActiveNotifications();

#if WINDOWS
                            if (device.DeviceSettings.IgnoreWindowsApps && await IsAppActiveAsync(appName ?? "")) return;
#endif

                            if (isNewToUser && notificationType == NotificationType.New)
                            {
                                var forceGamebar = generalSettings.GamebarRelayEnabled;
                                var overlayEnabled = generalSettings.DanmakuNotificationEnabled;

                                string? iconUrlForTcp = null;
                                byte[]? iconBytes = null;
                                if (!string.IsNullOrEmpty(appPackage))
                                {
                                    try
                                    {
                                        string iconFilePath = IconUtils.GetAppIconFilePath(appPackage);
                                        if (System.IO.File.Exists(iconFilePath))
                                        {
                                            iconBytes = System.IO.File.ReadAllBytes(iconFilePath);
                                            var ext = System.IO.Path.GetExtension(iconFilePath).ToLowerInvariant();
                                            string contentType = ext switch
                                            {
                                                ".png" => "image/png",
                                                ".jpg" or ".jpeg" => "image/jpeg",
                                                ".gif" => "image/gif",
                                                ".webp" => "image/webp",
                                                ".svg" => "image/svg+xml",
                                                _ => "application/octet-stream",
                                            };
                                            var b64 = Convert.ToBase64String(iconBytes);
                                            iconUrlForTcp = $"data:{contentType};base64,{b64}";
                                        }
                                    }
                                    catch (Exception ex)
                                    {
                                        logger.LogError(ex, "将图标编码为data URL失败");
                                    }
                                }

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

                    UpdateActiveNotifications();
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

        UpdateActiveNotifications();
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

                ClearBadge();

                UpdateActiveNotifications();
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

                UpdateActiveNotifications();
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

                ClearBadge();
                notificationRepository.ClearDeviceNotificationsExceptPinned(device.Id);

                UpdateActiveNotifications();
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "清除通知历史时出错");
            }
        });
    }



    private void UpdateActiveNotifications(PairedDevice? activeDevice = null)
    {
        dispatcher.EnqueueAsync(() =>
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

            // Update badge
            int totalNotifications = activeNotifications.Count;
            if (activeDevice?.DeviceSettings.ShowBadge == true)
            {
                XmlDocument badgeXml = BadgeUpdateManager.GetTemplateContent(BadgeTemplateType.BadgeNumber);
                XmlElement? badgeElement = badgeXml.SelectSingleNode("/badge") as XmlElement;
                badgeElement?.SetAttribute("value", totalNotifications.ToString());
                BadgeNotification badge = new(badgeXml);
                BadgeUpdater badgeUpdater = BadgeUpdateManager.CreateBadgeUpdaterForApplication();
                badgeUpdater.Update(badge);
            }

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

    private DateTime ParseNotificationTime(Notification notification)
    {
        if (notification.TimeStamp != null && long.TryParse(notification.TimeStamp, out var timestamp))
        {
            return DateTimeOffset.FromUnixTimeMilliseconds(timestamp).DateTime;
        }
        return DateTime.Now;
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
                    var msgJson = SocketMessageSerializer.DeserializeMessage(entity.MessageJson);
                    if (msgJson is null) continue;

                    var notif = await Notification.FromMessage(msgJson);
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

                    using var msgDoc = JsonDocument.Parse(msgJson);
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
                UpdateActiveNotifications();
            });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to load all notifications");
        }
    }

    /// <summary>
    /// Clears the badge number on the app tile
    /// </summary>
    private void ClearBadge()
    {
        try
        {
            _ = dispatcher.EnqueueAsync(() =>
            {
                BadgeUpdater badgeUpdater = BadgeUpdateManager.CreateBadgeUpdaterForApplication();
                badgeUpdater.Clear();
            });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "启动时清除角标失败");
        }
    }

#if WINDOWS
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
#endif

    /// <summary>
    /// 处理媒体播放通知
    /// </summary>
    /// <param name="device">设备</param>
    /// <param name="notificationMessage">通知消息</param>
    public async Task HandleMediaPlayNotification(PairedDevice device, string payload)
    {
        try
        {
            if (!device.DeviceSettings.NotificationSyncEnabled)
            {
                return;
            }

            using var doc = JsonDocument.Parse(payload);
            var root = doc.RootElement;

            var mediaType = root.TryGetProperty("mediaType", out var mtProp) ? mtProp.GetString() : null;
            var titleStr = root.TryGetProperty("title", out var tProp) ? tProp.GetString() ?? "" : "";
            var textStr = root.TryGetProperty("text", out var txProp) ? txProp.GetString() ?? "" : "";
            var coverUrl = root.TryGetProperty("coverUrl", out var cuProp) ? cuProp.GetString() : null
                ?? (root.TryGetProperty("bigPicture", out var bpProp) ? bpProp.GetString() : null)
                ?? (root.TryGetProperty("largeIcon", out var liProp) ? liProp.GetString() : null);

            // 解析播放状态：缺省视为播放中（与 SendMediaInfoAsync 行为一致）
            bool isPlaying = true;
            if (root.TryGetProperty("isPlaying", out var ipProp))
            {
                if (ipProp.ValueKind == JsonValueKind.False) isPlaying = false;
                else if (ipProp.ValueKind == JsonValueKind.True) isPlaying = true;
            }

            // 叠加层媒体卡片开关（与本地媒体一致）
            var mediaOverlayEnabled = generalSettings.DanmakuMediaCardEnabled;

            if (mediaType == "END")
            {
                await dispatcher.EnqueueAsync(async () =>
                {
                    try
                    {
                        var existingBlock = _currentMusicMediaBlocks.FirstOrDefault(b => b.DeviceId == device.Id);
                        if (existingBlock != null)
                        {
                            _currentMusicMediaBlocks.Remove(existingBlock);
                            _ = LocalSocketRelayServer.SendMediaInfoAsync(device.Id, device.Name, "", "", "", false);
                        }

                        if (mediaOverlayEnabled)
                        {
                            overlayRender.RemoveMediaCard(device.Id);
                        }
                    }
                    catch (Exception ex)
                    {
                        logger.LogError(ex, "移除媒体块时出错，设备：{deviceId}", device.Id);
                    }
                });
                return;
            }

            await dispatcher.EnqueueAsync(async () =>
            {
                try
                {
                    var existingBlock = _currentMusicMediaBlocks.FirstOrDefault(b => b.DeviceId == device.Id);
                    if (existingBlock == null)
                    {
                        var newBlock = new MusicMediaBlock(
                            device.Id,
                            device.Name,
                            titleStr,
                            textStr,
                            coverUrl
                        );
                        _currentMusicMediaBlocks.Add(newBlock);
                        _ = LocalSocketRelayServer.SendMediaInfoAsync(device.Id, device.Name, titleStr, textStr, coverUrl ?? "", true);

                        if (mediaOverlayEnabled)
                        {
                            var coverBytes = ConvertCoverUrlToBytes(coverUrl);
                            overlayRender.ShowMediaCard(device.Id, device.Name, titleStr, textStr, coverBytes, isPlaying);
                        }
                    }
                    else
                    {
                        string updatedTitle = existingBlock.Title;
                        string updatedText = existingBlock.Text;
                        string? updatedCoverUrl = existingBlock.CoverUrl;

                        if (!string.IsNullOrEmpty(titleStr))
                        {
                            updatedTitle = titleStr;
                        }

                        if (!string.IsNullOrEmpty(textStr))
                        {
                            updatedText = textStr;
                        }

                        if (!string.IsNullOrEmpty(coverUrl))
                        {
                            updatedCoverUrl = coverUrl;
                        }

                        existingBlock.Update(updatedTitle, updatedText, updatedCoverUrl);
                        _ = LocalSocketRelayServer.SendMediaInfoAsync(device.Id, device.Name, updatedTitle, updatedText, updatedCoverUrl ?? "", true);

                        if (mediaOverlayEnabled)
                        {
                            var coverBytes = ConvertCoverUrlToBytes(updatedCoverUrl);
                            overlayRender.ShowMediaCard(device.Id, device.Name, updatedTitle, updatedText, coverBytes, isPlaying);
                        }
                    }
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "在UI线程上处理媒体播放通知时出错");
                }
            });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "处理媒体播放通知时出错");
        }
    }

    /// <summary>
    /// 将封面 URL（Data URL 或纯 base64）转换为字节数组，失败返回 null。
    /// </summary>
    private static byte[]? ConvertCoverUrlToBytes(string? coverUrl)
    {
        if (string.IsNullOrEmpty(coverUrl)) return null;
        try
        {
            var base64 = coverUrl.Contains(',') ? coverUrl.Split(',')[1] : coverUrl;
            return Convert.FromBase64String(base64);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// 检查音乐媒体块是否超时
    /// </summary>
    public void CheckMusicMediaBlockTimeout()
    {
        dispatcher.EnqueueAsync(async () =>
        {
            // 检查集合中每个媒体块是否超时，超时则移除
            var toRemove = _currentMusicMediaBlocks.Where(b => b.IsTimeout(MUSIC_MEDIA_BLOCK_TIMEOUT)).ToList();
            foreach (var b in toRemove)
            {
                try
                {
                    _currentMusicMediaBlocks.Remove(b);

                    if (generalSettings.DanmakuMediaCardEnabled)
                    {
                        overlayRender.RemoveMediaCard(b.DeviceId);
                    }
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "移除超时的音乐媒体块时出错，设备：{deviceId}", b.DeviceId);
                }

                _ = LocalSocketRelayServer.SendMediaInfoAsync(b.DeviceId, b.DeviceName, "", "", "", false);
            }
        });
    }

    /// <summary>
    /// 处理图标响应，通知等待的图标请求任务
    /// </summary>
    /// <param name="deviceId">设备 ID</param>
    /// <param name="packageName">应用包名</param>
    public void HandleIconResponse(string deviceId, string packageName)
    {
        try
        {
            string requestKey = $"{packageName}|{deviceId}";
            if (pendingIconRequests.TryGetValue(requestKey, out var tcs))
            {
                // 完成等待的任务
                tcs.TrySetResult(true);
                logger.LogDebug("已通知图标请求完成：{PackageName}", packageName);
            }

            // 更新所有使用该包名的通知的图标
            dispatcher.EnqueueAsync(async () =>
            {
                var notificationsToUpdate = activeNotifications.Where(n => n.AppPackage == packageName).ToList();
                foreach (var notification in notificationsToUpdate)
                {
                    // 更新图标路径和图标
                    notification.IconPath = IconUtils.GetAppIconPath(packageName);
                    await notification.LoadIconAsync();
                }

                // 刷新所有通知
                UpdateActiveNotifications();
            });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "处理图标响应通知时出错");
        }
    }

    public async Task ProcessMediaPlayMessageAsync(PairedDevice device, string payload)
    {
        try
        {
            logger.LogTrace("收到DATA_MEDIAPLAY消息，设备：{deviceId}", device.Id);

            // 检查是否为结束包
            using var doc = JsonDocument.Parse(payload);
            var root = doc.RootElement;
            var terminateValue = root.TryGetProperty("terminateValue", out var tv) && tv.ValueKind == JsonValueKind.String ? tv.GetString() : null;

            string finalPayload = payload;
            if (terminateValue != null && terminateValue.Equals("__END__", StringComparison.OrdinalIgnoreCase))
            {
                // 构造结束标记payload
                var rawJson = JsonSerializer.Serialize(new { type = "DATA_MEDIAPLAY", mediaType = "END", time = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() });
                var validatedJson = rawJson;
                if (validatedJson != null) finalPayload = validatedJson;
            }

            await HandleMediaPlayNotification(device, finalPayload);
        }
        catch (JsonException jsonEx)
        {
            logger.LogError(jsonEx, "解析DATA_MEDIAPLAY消息JSON时出错，消息内容：{payload}", payload.Length > 100 ? payload[..100] + "..." : payload);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "处理DATA_MEDIAPLAY消息时出错");
        }
    }

    public async Task ProcessIconResponseAsync(PairedDevice device, string payload)
    {
        try
        {
            if (!payload.TrimStart().StartsWith('{') && !payload.TrimStart().StartsWith('['))
            {
                logger.LogWarning("跳过非 JSON 图标响应：{payload}", payload.Length > 50 ? payload[..50] + "..." : payload);
                return;
            }

            // 首先尝试解析JSON
            using var doc = JsonDocument.Parse(payload);
            var root = doc.RootElement;

            logger.LogInformation("处理ICON_RESPONSE消息");

            // 检查是否为批量图标响应
            if (root.TryGetProperty("icons", out var iconsArray))
            {
                // 处理批量图标响应
                logger.LogInformation("接收到批量图标响应，包含 {count} 个图标", iconsArray.GetArrayLength());
                int savedCount = 0;
                foreach (var iconElement in iconsArray.EnumerateArray())
                {
                    // 获取包名
                    if (!iconElement.TryGetProperty("packageName", out var packageProp))
                    {
                        logger.LogWarning("批量 ICON_RESPONSE 中的图标缺少 packageName 属性");
                        continue;
                    }

                    var packageName = packageProp.GetString();
                    if (string.IsNullOrEmpty(packageName))
                    {
                        logger.LogWarning("批量 ICON_RESPONSE 中的图标 packageName 为空");
                        continue;
                    }

                    // 获取图标数据
                    if (!iconElement.TryGetProperty("iconData", out var iconDataProp))
                    {
                        logger.LogWarning("批量 ICON_RESPONSE 中的图标缺少 iconData 属性");
                        continue;
                    }

                    var iconData = iconDataProp.GetString();
                    if (string.IsNullOrEmpty(iconData))
                    {
                        logger.LogWarning("批量 ICON_RESPONSE 中的图标 iconData 为空");
                        continue;
                    }

                    logger.LogInformation("正在保存应用 {packageName} 的图标，数据长度：{length}", packageName, iconData.Length);
                    // 保存图标
                    await IconUtils.SaveAppIconToPathAsync(iconData, packageName);
                    savedCount++;

                    // 触发应用图标更新
                    HandleIconResponse(device.Id, packageName);
                }
                logger.LogInformation("批量图标响应处理完成，已保存 {savedCount} 个应用图标", savedCount);
            }
            else
            {
                // 处理单个图标响应
                // 直接调用IconUtils保存图标
                var packageName = root.TryGetProperty("packageName", out var packageNameProp) ? packageNameProp.GetString() : null;
                var iconData = root.TryGetProperty("iconData", out var iconDataProp) ? iconDataProp.GetString() : null;

                if (!string.IsNullOrEmpty(packageName) && !string.IsNullOrEmpty(iconData))
                {
                    logger.LogInformation("正在保存应用 {packageName} 的图标，数据长度：{length}", packageName, iconData.Length);
                    await IconUtils.SaveAppIconToPathAsync(iconData, packageName);
                    logger.LogInformation("已保存应用图标：{packageName}", packageName);
                    // 触发应用图标更新
                    HandleIconResponse(device.Id, packageName);
                }
                else
                {
                    logger.LogWarning("单个图标响应缺少必要属性：packageName={packageName}, iconData={iconData}", packageName, iconData);
                }
            }
        }
        catch (JsonException ex)
        {
            logger.LogWarning("解析图标响应JSON时出错：{ex.Message}", ex.Message);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "处理图标响应时出错");
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
