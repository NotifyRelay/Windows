using CommunityToolkit.WinUI;
using NotifyRelay.Data.AppDatabase.Repository;
using NotifyRelay.Data.Contracts;
using NotifyRelay.Data.Enums;
using NotifyRelay.Data.Models;
using NotifyRelay.Utils;
using NotifyRelay.Utils.Serialization;
using Windows.Data.Xml.Dom;
using Windows.System;
using Windows.UI.Notifications;
using Notification = NotifyRelay.Data.Models.Notification;

namespace NotifyRelay.Services;
public class NotificationService(
    ILogger logger,
    ISessionManager sessionManager,
    IDeviceManager deviceManager,
    IPlatformNotificationHandler platformNotificationHandler,
    RemoteAppRepository remoteAppsRepository,
    NotificationRepository notificationRepository,
    Func<INetworkService> networkServiceFactory,
    Func<IRemoteAppService> remoteAppServiceFactory,
    IPlaybackService playbackService) : INotificationService, INotifyPropertyChanged
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

    private void OnSocketCommandReceived(object? sender, string commandJson)
    {
        try
        {
            using var doc = JsonDocument.Parse(commandJson);
            var root = doc.RootElement;
            if (root.TryGetProperty("action", out var actionProp) && actionProp.GetString() == "media_control")
            {
                var deviceId = root.GetProperty("deviceId").GetString();
                var command = root.GetProperty("command").GetString();
                if (!string.IsNullOrEmpty(deviceId) && !string.IsNullOrEmpty(command))
                {
                    playbackService.SendMediaControlRequest(deviceId, command);
                }
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "处理Socket指令失败");
        }
    }





    public async Task HandleNotificationMessage(PairedDevice device, NotificationMessage message)
    {
        logger.LogDebug("收到通知消息: NotificationType={NotificationType}, Title={Title}, AppPackage={AppPackage}, AppName={AppName}, Text={Text}",
            message.NotificationType, message.Title, message.AppPackage, message.AppName, message.Text);

        // Check if device has notification sync enabled
        if (!device.DeviceSettings.NotificationSyncEnabled)
        {
            logger.LogDebug("设备通知同步已禁用，跳过通知");
            return;
        }

        try
        {
            // 过滤超级岛通知，识别段是'superisland:'
            if (message.AppPackage?.StartsWith("superisland:") == true)
            {
                return;
            }

            if (message.NotificationType == NotificationType.Removed)
            {
                await dispatcher.EnqueueAsync(() =>
                {
                    // Find matching notification
                    var notification = activeNotifications.FirstOrDefault(n =>
                        n.Key == message.NotificationKey ||
                        (n.AppPackage == message.AppPackage &&
                         n.Title == message.Title &&
                         n.Text == message.Text));

                    if (notification != null && !notification.Pinned)
                    {
                        // Remove source device
                        var source = notification.SourceDevices.FirstOrDefault(sd => sd.DeviceId == device.Id);
                        if (source != null)
                        {
                            notification.SourceDevices.Remove(source);
                        }

                        // If no sources left, remove from activeNotifications
                        if (notification.SourceDevices.Count == 0)
                        {
                            activeNotifications.Remove(notification);
                        }

                        notificationRepository.DeleteNotification(device.Id, message.NotificationKey);
                        UpdateActiveNotifications();
                    }
                });
                return;
            }

            if (message.Title is not null && message.AppPackage is not null)
            {
                var filter = remoteAppsRepository.GetAppNotificationFilterAsync(message.AppPackage, device.Id)
                ?? await remoteAppsRepository.AddOrUpdateApplicationForDevice(device.Id, message.AppPackage, message.AppName!, message.AppIcon);

                if (filter == NotificationFilter.Disabled) return;

                // 检查是否需要请求图标
                bool needIconRequest = !string.IsNullOrEmpty(message.AppPackage) && !IconUtils.AppIconExists(message.AppPackage);
                TaskCompletionSource<bool>? iconRequestTcs = null;
                string? requestKey = null;

                if (needIconRequest)
                {
                    requestKey = $"{message.AppPackage}|{device.Id}";
                    iconRequestTcs = new TaskCompletionSource<bool>();
                    pendingIconRequests[requestKey] = iconRequestTcs;
                    remoteAppServiceFactory().SendIconRequest(device.Id, [message.AppPackage]);
                }

                if (iconRequestTcs != null)
                {
                    var timeoutTask = Task.Delay(ICON_REQUEST_TIMEOUT);
                    var completedTask = await Task.WhenAny(iconRequestTcs.Task, timeoutTask);
                    if (requestKey != null) pendingIconRequests.Remove(requestKey);
                }

                await dispatcher.EnqueueAsync(async () =>
                        {
                            // 检查是否存在内容相同的现有通知
                            var existingNotification = activeNotifications.FirstOrDefault(n =>
                                n.AppPackage == message.AppPackage &&
                                n.Title == message.Title &&
                                n.Text == message.Text &&
                                n.Type == message.NotificationType);

                            bool isNewToUser = existingNotification is null;
                            Notification notification;

                            if (existingNotification != null)
                            {
                                notification = existingNotification;
                                // 避免重复添加同一设备
                                if (!notification.SourceDevices.Any(sd => sd.DeviceId == device.Id))
                                {
                                    notification.AddSourceDevice(device.Id, device.Name);
                                }

                                if (notification.Icon == null && !string.IsNullOrEmpty(message.AppPackage))
                                {
                                    notification.IconPath = IconUtils.GetAppIconPath(message.AppPackage);
                                    if (IconUtils.AppIconExists(message.AppPackage)) await notification.LoadIconAsync();
                                }
                            }
                            else
                            {
                                notification = await Notification.FromMessage(message);
                                notification.AddSourceDevice(device.Id, device.Name);
                                if (!string.IsNullOrEmpty(message.AppPackage))
                                {
                                    notification.IconPath = IconUtils.GetAppIconPath(message.AppPackage);
                                    if (IconUtils.AppIconExists(message.AppPackage)) await notification.LoadIconAsync();
                                }
                                activeNotifications.Add(notification);
                            }

                            // 更新数据库
                            bool shouldSave = true;
                            if (message.NotificationType != NotificationType.New && filter != NotificationFilter.ToastFeed && filter != NotificationFilter.Feed)
                            {
                                shouldSave = false;
                            }

                            if (shouldSave)
                            {
                                notificationRepository.UpsertNotification(device.Id, message, notification.Pinned);
                            }

                            UpdateActiveNotifications();

#if WINDOWS
                            if (device.DeviceSettings.IgnoreWindowsApps && await IsAppActiveAsync(message.AppName!)) return;
#endif

                            // 只有当通知是新的时，才会发送到Windows通知中心
                            if (isNewToUser && message.NotificationType == NotificationType.New)
                            {
                                bool tcpSentSuccessfully = false;
                                try
                                {
                                    string? iconUrl = null;
                                    if (!string.IsNullOrEmpty(message.AppPackage))
                                    {
                                        try
                                        {
                                            string iconFilePath = IconUtils.GetAppIconFilePath(message.AppPackage);
                                            if (System.IO.File.Exists(iconFilePath))
                                            {
                                                var bytes = System.IO.File.ReadAllBytes(iconFilePath);
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
                                                var b64 = Convert.ToBase64String(bytes);
                                                iconUrl = $"data:{contentType};base64,{b64}";
                                            }
                                        }
                                        catch (Exception ex)
                                        {
                                            logger.LogError(ex, "将图标编码为data URL失败");
                                        }
                                    }

                                    tcpSentSuccessfully = await LocalSocketRelayServer.SendNotificationAsync(
                                        message.AppName!,
                                        message.AppPackage!,
                                        message.Title!,
                                        message.Text ?? string.Empty,
                                        iconUrl);
                                }
                                catch (Exception ex)
                                {
                                    logger.LogError(ex, "发送TCP通知失败");
                                }

                                if (!tcpSentSuccessfully)
                                {
                                    await platformNotificationHandler.ShowRemoteNotification(message, device.Id);
                                }
                                else
                                {
                                    logger.LogDebug("TCP通知发送成功，跳过Windows系统通知");
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
                XmlElement badgeElement = badgeXml.SelectSingleNode("/badge") as XmlElement;
                badgeElement.SetAttribute("value", totalNotifications.ToString());
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
                    var msg = SocketMessageSerializer.DeserializeMessage(entity.MessageJson) as NotificationMessage;
                    if (msg is null) continue;

                    var notif = await Notification.FromMessage(msg);
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

                    if (!string.IsNullOrEmpty(msg.AppPackage))
                    {
                        string iconPath = IconUtils.GetAppIconPath(msg.AppPackage);
                        notif.IconPath = iconPath;
                        if (IconUtils.AppIconExists(msg.AppPackage))
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
    public async Task HandleMediaPlayNotification(PairedDevice device, NotificationMessage notificationMessage)
    {
        // logger.LogDebug("进入HandleMediaPlayNotification方法，设备：{deviceId}", device.Id);
        try
        {
            // 检查设备是否启用了通知同步
            // logger.LogDebug("检查设备通知同步设置，设备ID：{deviceId}，是否启用：{enabled}", device.Id, device.DeviceSettings.NotificationSyncEnabled);
            if (!device.DeviceSettings.NotificationSyncEnabled)
            {
                // logger.LogDebug("设备通知同步未启用，跳过处理媒体播放通知");
                return;
            }

            // 检查是否为媒体结束包
            if (notificationMessage.MediaType == "END")
            {
                // logger.LogDebug("收到媒体结束包，移除设备：{deviceId}的媒体块", device.Id);
                // 所有对_currentMusicMediaBlocks集合的访问都必须在UI线程上进行
                await dispatcher.EnqueueAsync(async () =>
                {
                    try
                    {
                        // 查找并移除对应设备的媒体块
                        var existingBlock = _currentMusicMediaBlocks.FirstOrDefault(b => b.DeviceId == device.Id);
                        if (existingBlock != null)
                        {
                            _currentMusicMediaBlocks.Remove(existingBlock);
                            // logger.LogDebug("已移除设备：{deviceId}的媒体块", device.Id);
                            _ = LocalSocketRelayServer.SendMediaInfoAsync(device.Id, device.Name, "", "", "", false);
                        }
                    }
                    catch (Exception ex)
                    {
                        logger.LogError(ex, "移除媒体块时出错，设备：{deviceId}", device.Id);
                    }
                });
                return;
            }

            // 解析媒体播放通知的标题和文本
            // 注意：对于差异包，我们需要保留现有值，而不是将缺失的字段置空
            string title = notificationMessage.Title ?? "";
            string text = notificationMessage.Text ?? "";

            // logger.LogDebug("媒体播放通知内容：标题='{title}', 文本='{text}'", title, text);

            // 从通知消息中提取封面URL
            string? coverUrl = null;
            if (!string.IsNullOrEmpty(notificationMessage.CoverUrl))
            {
                coverUrl = notificationMessage.CoverUrl;
                // logger.LogDebug("从CoverUrl提取封面：{coverUrl}", coverUrl);
            }
            else if (!string.IsNullOrEmpty(notificationMessage.BigPicture))
            {
                coverUrl = notificationMessage.BigPicture;
                // logger.LogDebug("从BigPicture提取封面：{coverUrl}", coverUrl);
            }
            else if (!string.IsNullOrEmpty(notificationMessage.LargeIcon))
            {
                coverUrl = notificationMessage.LargeIcon;
                // logger.LogDebug("从LargeIcon提取封面：{coverUrl}", coverUrl);
            }
            else
            {
                // logger.LogDebug("未找到封面URL");
            }

            // 所有对_currentMusicMediaBlocks集合的访问都必须在UI线程上进行
            await dispatcher.EnqueueAsync(async () =>
            {
                try
                {
                    // 更新或创建音乐媒体块（支持多个设备）
                    // logger.LogDebug("当前MusicMediaBlocks 数量：{count}", _currentMusicMediaBlocks.Count);
                    var existingBlock = _currentMusicMediaBlocks.FirstOrDefault(b => b.DeviceId == device.Id);
                    if (existingBlock == null)
                    {
                        // 创建新的音乐媒体块并加入集合
                        // logger.LogDebug("创建新的音乐媒体块，设备：{deviceId}", device.Id);
                        var newBlock = new MusicMediaBlock(
                            device.Id,
                            device.Name,
                            title,
                            text,
                            coverUrl
                        );
                        _currentMusicMediaBlocks.Add(newBlock);
                        // logger.LogDebug("新音乐媒体块已加入集合");
                        _ = LocalSocketRelayServer.SendMediaInfoAsync(device.Id, device.Name, title, text, coverUrl ?? "", true);
                    }
                    else
                    {
                        // 处理差异包：只更新那些在通知消息中明确提供的字段
                        // logger.LogDebug("更新现有音乐媒体块，设备：{deviceId}", device.Id);
                        string updatedTitle = existingBlock.Title;
                        string updatedText = existingBlock.Text;
                        string? updatedCoverUrl = existingBlock.CoverUrl;

                        if (!string.IsNullOrEmpty(notificationMessage.Title))
                        {
                            updatedTitle = notificationMessage.Title;
                            // logger.LogDebug("更新标题：{updatedTitle}", updatedTitle);
                        }

                        if (!string.IsNullOrEmpty(notificationMessage.Text))
                        {
                            updatedText = notificationMessage.Text;
                            // logger.LogDebug("更新文本：{updatedText}", updatedText);
                        }

                        if (!string.IsNullOrEmpty(coverUrl))
                        {
                            updatedCoverUrl = coverUrl;
                            // logger.LogDebug("更新封面URL：{updatedCoverUrl}", updatedCoverUrl);
                        }

                        // 直接更新音乐媒体块的属性
                        existingBlock.Update(updatedTitle, updatedText, updatedCoverUrl);
                        // logger.LogDebug("音乐媒体块更新完成");
                        _ = LocalSocketRelayServer.SendMediaInfoAsync(device.Id, device.Name, updatedTitle, updatedText, updatedCoverUrl ?? "", true);
                    }

                    // logger.LogDebug("媒体播放通知处理完成");
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

            // 直接使用JsonDocument解析DATA_MEDIAPLAY消息
            using JsonDocument doc = JsonDocument.Parse(payload);
            JsonElement root = doc.RootElement;

            // 提取time字段，处理Number和String两种类型
            string timeStamp;
            if (root.TryGetProperty("time", out JsonElement timeElement))
            {
                if (timeElement.ValueKind == JsonValueKind.String)
                {
                    timeStamp = timeElement.GetString() ?? DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString();
                }
                else if (timeElement.ValueKind == JsonValueKind.Number)
                {
                    timeStamp = timeElement.GetInt64().ToString();
                }
                else
                {
                    timeStamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString();
                }
            }
            else if (root.TryGetProperty("timeStamp", out JsonElement timeStampElement))
            {
                if (timeStampElement.ValueKind == JsonValueKind.String)
                {
                    timeStamp = timeStampElement.GetString() ?? DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString();
                }
                else if (timeStampElement.ValueKind == JsonValueKind.Number)
                {
                    timeStamp = timeStampElement.GetInt64().ToString();
                }
                else
                {
                    timeStamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString();
                }
            }
            else
            {
                timeStamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString();
            }

            // 检查是否为结束包
            var mediaType = root.TryGetProperty("mediaType", out JsonElement mediaTypeElement) && mediaTypeElement.ValueKind == JsonValueKind.String ? mediaTypeElement.GetString() : null;
            var terminateValue = root.TryGetProperty("terminateValue", out JsonElement terminateValueElement) && terminateValueElement.ValueKind == JsonValueKind.String ? terminateValueElement.GetString() : null;

            // 如果terminateValue为__END__，则设置为结束包
            if (terminateValue != null && terminateValue.Equals("__END__", StringComparison.OrdinalIgnoreCase))
            {
                mediaType = "END";
            }

            // 直接构造NotificationMessage对象
            var notificationMessage = new NotificationMessage
            {
                NotificationKey = Guid.NewGuid().ToString(),
                TimeStamp = timeStamp,
                NotificationType = NotificationType.New,
                AppPackage = root.TryGetProperty("packageName", out JsonElement packageNameElement) && packageNameElement.ValueKind == JsonValueKind.String ? packageNameElement.GetString() : null,
                AppName = root.TryGetProperty("appName", out JsonElement appNameElement) && appNameElement.ValueKind == JsonValueKind.String ? appNameElement.GetString() : null,
                Title = root.TryGetProperty("title", out JsonElement titleElement) && titleElement.ValueKind == JsonValueKind.String ? titleElement.GetString() : null,
                Text = root.TryGetProperty("text", out JsonElement textElement) && textElement.ValueKind == JsonValueKind.String ? textElement.GetString() : null,
                BigPicture = root.TryGetProperty("bigPicture", out JsonElement bigPictureElement) && bigPictureElement.ValueKind == JsonValueKind.String ? bigPictureElement.GetString() : null,
                LargeIcon = root.TryGetProperty("largeIcon", out JsonElement largeIconElement) && largeIconElement.ValueKind == JsonValueKind.String ? largeIconElement.GetString() : null,
                CoverUrl = root.TryGetProperty("coverUrl", out JsonElement coverUrlElement) && coverUrlElement.ValueKind == JsonValueKind.String ? coverUrlElement.GetString() : null,
                MediaType = mediaType
            };

            await HandleMediaPlayNotification(device, notificationMessage);
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

            // 首先尝试解析JSON
            using var doc = JsonDocument.Parse(payload);
            var root = doc.RootElement;

            logger.LogDebug("处理普通通知消息");
            // 创建NotificationMessage对象
            var notificationMessage = new NotificationMessage
            {
                NotificationKey = root.TryGetProperty("notificationKey", out var keyProp) && keyProp.ValueKind == JsonValueKind.String ?
                    keyProp.GetString() : Guid.NewGuid().ToString(),
                TimeStamp = root.TryGetProperty("timeStamp", out var timeProp) && timeProp.ValueKind == JsonValueKind.String ?
                    timeProp.GetString() : DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString(),
                NotificationType = root.TryGetProperty("notificationType", out var typeProp) && typeProp.ValueKind == JsonValueKind.String ?
                    Enum.TryParse<NotificationType>(typeProp.GetString(), true, out var type) ? type : NotificationType.New : NotificationType.New,
                // 同时尝试获取packageName和appPackage字段
                AppPackage = (root.TryGetProperty("packageName", out var notificationPackageNameProp) && notificationPackageNameProp.ValueKind == JsonValueKind.String ? notificationPackageNameProp.GetString() : null) ??
                            (root.TryGetProperty("appPackage", out var appPackageProp) && appPackageProp.ValueKind == JsonValueKind.String ? appPackageProp.GetString() : null),
                AppName = root.TryGetProperty("appName", out var appNameProp) && appNameProp.ValueKind == JsonValueKind.String ? appNameProp.GetString() : null,
                Title = root.TryGetProperty("title", out var titleProp) && titleProp.ValueKind == JsonValueKind.String ? titleProp.GetString() : null,
                Text = root.TryGetProperty("text", out var textProp) && textProp.ValueKind == JsonValueKind.String ? textProp.GetString() : null,
                BigPicture = root.TryGetProperty("bigPicture", out var bigPictureProp) && bigPictureProp.ValueKind == JsonValueKind.String ? bigPictureProp.GetString() : null,
                LargeIcon = root.TryGetProperty("largeIcon", out var largeIconProp) && largeIconProp.ValueKind == JsonValueKind.String ? largeIconProp.GetString() : null,
                CoverUrl = root.TryGetProperty("coverUrl", out var coverUrlProp) && coverUrlProp.ValueKind == JsonValueKind.String ? coverUrlProp.GetString() : null,
                MediaType = root.TryGetProperty("mediaType", out var mediaTypeProp) && mediaTypeProp.ValueKind == JsonValueKind.String ? mediaTypeProp.GetString() : null
            };

            // 调用消息处理器处理通知消息
            await HandleNotificationMessage(device, notificationMessage);
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
