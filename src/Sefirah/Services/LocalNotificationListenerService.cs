using System.Reflection;
using NotifyRelay.Data.AppDatabase.Repository;
using NotifyRelay.Data.Contracts;
using NotifyRelay.Data.Enums;
using NotifyRelay.Native;
using NotifyRelay.Services.Filters;
using Windows.Storage.Streams;

namespace NotifyRelay.Services;

public class LocalNotificationListenerService : ILocalNotificationListenerService, IDisposable
{
    private readonly ILogger _logger;
    private readonly ISessionManager _sessionManager;
    private readonly NotificationRepository _notificationRepository;
    private readonly IDeviceManager _deviceManager;

    private string? _localDeviceId;
    public static event Action? LocalNotificationCaptured;

    // 反射缓存的元数据（惰性初始化，Start() 调用时解析）
    private static Type? _listenerType;
    private static PropertyInfo? _currentProperty;
    private static MethodInfo? _getNotificationsAsyncMethod;
    private static MethodInfo? _requestAccessAsyncMethod;
    private static bool? _isSupported;
    private static readonly object _initLock = new();

    private static bool CheckSupported()
    {
        if (_isSupported.HasValue) return _isSupported.Value;

        lock (_initLock)
        {
            if (_isSupported.HasValue) return _isSupported.Value;

            try
            {
                // 搜索所有已加载程序集（此时 WinRT 程序集应已加载）
                foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
                {
                    var type = asm.GetType("Windows.UI.Notifications.UserNotificationListener");
                    if (type == null) continue;

                    _listenerType = type;
                    _currentProperty = type.GetProperty("Current", BindingFlags.Static | BindingFlags.Public);
                    _getNotificationsAsyncMethod = type.GetMethod("GetNotificationsAsync");
                    _requestAccessAsyncMethod = type.GetMethod("RequestAccessAsync");

                    if (_currentProperty != null)
                    {
                        _isSupported = true;
                        return true;
                    }
                }
            }
            catch { }

            _isSupported = false;
            return false;
        }
    }

    private object? _listenerInstance;
    private Timer? _pollTimer;
    private readonly HashSet<uint> _knownNotificationIds = [];
    private bool _isRunning;
    private bool _disposed;

    public LocalNotificationListenerService(
        ILogger<LocalNotificationListenerService> logger,
        ISessionManager sessionManager,
        NotificationRepository notificationRepository,
        IDeviceManager deviceManager)
    {
        _logger = logger;
        _sessionManager = sessionManager;
        _notificationRepository = notificationRepository;
        _deviceManager = deviceManager;
    }

    public static bool IsSupported => CheckSupported();

    public void Start()
    {
        if (_isRunning) return;
        _ = InitializeAsync();
    }

    private async Task InitializeAsync()
    {
        if (!IsSupported)
        {
            _logger.LogWarning("当前平台不支持 UserNotificationListener");
            return;
        }

        try
        {
            _listenerInstance = _currentProperty!.GetValue(null);
            if (_listenerInstance == null)
            {
                _logger.LogWarning("UserNotificationListener.Current 返回 null");
                return;
            }

            // 通过反射调用 RequestAccessAsync（AccessStatus 在同一 WinMD 中）
            var accessStatusType = _listenerType?.Assembly.GetType("Windows.UI.Notifications.UserNotificationListenerAccessStatus");
            var allowedField = accessStatusType?.GetField("Allowed");

            var accessTask = (Task)_requestAccessAsyncMethod!.Invoke(_listenerInstance, [])!;
            await accessTask;
            var accessStatus = accessTask.GetType().GetProperty("Result")?.GetValue(accessTask);

            if (accessStatus == null || !accessStatus.Equals(allowedField?.GetValue(null)))
            {
                _logger.LogWarning("UserNotificationListener 访问被拒绝 ({AccessStatus})，请在系统设置 → 通知中允许此应用", accessStatus);
            }
            else
            {
                _logger.LogInformation("UserNotificationListener 访问已授权");
            }

            try
            {
                var device = await _deviceManager.GetLocalDeviceAsync();
                _localDeviceId = device?.DeviceId;
                _logger.LogInformation("本地设备 ID: {DeviceId}", _localDeviceId);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "获取本地设备 ID 失败");
            }

            await LoadExistingNotificationsAsync();

            _pollTimer = new Timer(
                _ => _ = PollNotificationsAsync(),
                null,
                TimeSpan.FromSeconds(1),
                TimeSpan.FromSeconds(1.5));

            _isRunning = true;
            _logger.LogInformation("LocalNotificationListenerService 启动成功（轮询模式）");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "启动 LocalNotificationListenerService 失败");
        }
    }

    public void Stop()
    {
        if (!_isRunning) return;

        _pollTimer?.Dispose();
        _pollTimer = null;
        _isRunning = false;
        _logger.LogInformation("LocalNotificationListenerService 已停止");
    }

    private async Task LoadExistingNotificationsAsync()
    {
        try
        {
            if (_listenerInstance == null || _getNotificationsAsyncMethod == null) return;

            var notifications = await GetNotificationsAsync();
            if (notifications == null) return;

            var sorted = notifications
                .OrderByDescending(n => n.CreationTime)
                .Take(20)
                .ToList();

            _logger.LogInformation("现有通知数量: {Total}, 将处理: {Count}", notifications.Count, sorted.Count);

            lock (_knownNotificationIds)
            {
                _knownNotificationIds.Clear();
                foreach (var notif in sorted)
                    _knownNotificationIds.Add(notif.Id);
            }

            foreach (var notif in sorted)
                ProcessNotification(notif);

            _logger.LogDebug("已加载 {Count} 个现有通知", _knownNotificationIds.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "加载现有通知失败");
        }
    }

    private async Task PollNotificationsAsync()
    {
        try
        {
            if (_listenerInstance == null || _getNotificationsAsyncMethod == null) return;

            var notifications = await GetNotificationsAsync();
            if (notifications == null) return;

            var currentIds = new HashSet<uint>();
            foreach (var notif in notifications)
            {
                currentIds.Add(notif.Id);

                bool isNew;
                lock (_knownNotificationIds) { isNew = _knownNotificationIds.Add(notif.Id); }

                if (isNew)
                    ProcessNotification(notif);
            }

            List<uint> removedIds;
            lock (_knownNotificationIds)
            {
                removedIds = _knownNotificationIds.Except(currentIds).ToList();
                foreach (var id in removedIds)
                    _knownNotificationIds.Remove(id);
            }

            foreach (var id in removedIds)
                HandleRemovedNotification(id);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "轮询通知失败");
        }
    }

    private async Task<IReadOnlyList<Windows.UI.Notifications.UserNotification>?> GetNotificationsAsync()
    {
        if (_listenerInstance == null || _getNotificationsAsyncMethod == null) return null;

        var task = (Task)_getNotificationsAsyncMethod.Invoke(
            _listenerInstance, [Windows.UI.Notifications.NotificationKinds.Toast])!;
        await task;
        return task.GetType().GetProperty("Result")?.GetValue(task)
            as IReadOnlyList<Windows.UI.Notifications.UserNotification>;
    }

    private void ProcessNotification(Windows.UI.Notifications.UserNotification userNotification)
    {
        try
        {
            var appInfo = userNotification.AppInfo;
            if (appInfo == null)
            {
                _logger.LogWarning("通知缺少 AppInfo");
                return;
            }

            var appPackage = appInfo.AppUserModelId ?? appInfo.PackageFamilyName ?? "unknown";
            var appName = appInfo.DisplayInfo?.DisplayName ?? appPackage;
            var id = userNotification.Id;
            var notification = userNotification.Notification;

            var title = "";
            var text = "";

            if (notification?.Visual?.Bindings is { Count: > 0 })
            {
                var binding = notification.Visual.Bindings[0];
                var textElements = binding.GetTextElements();

                if (textElements.Count > 0)
                    title = textElements[0].Text ?? "";

                if (textElements.Count > 1)
                {
                    var parts = new List<string>();
                    for (int i = 1; i < textElements.Count; i++)
                    {
                        var t = textElements[i].Text;
                        if (!string.IsNullOrWhiteSpace(t))
                            parts.Add(t);
                    }
                    text = string.Join("\n", parts);
                }
            }

            if (!BackendLocalFilter.ShouldForward(appName, appPackage, title, text))
                return;

            var isLocked = IsWorkstationLocked();
            var appIconBase64 = ExtractAppIconAsync(userNotification).GetAwaiter().GetResult();

            var rawJson = JsonSerializer.Serialize(new
            {
                type = "DATA_NOTIFICATION",
                notificationKey = $"local_{id}",
                timeStamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString(),
                notificationType = "New",
                appPackage = appPackage,
                appName = appName,
                title = title,
                text = text,
                appIcon = appIconBase64,
                isLocked = isLocked
            });
            var json = rawJson;
            if (json == null) return;
            _sessionManager.BroadcastMessage(json);

            if (_localDeviceId != null)
            {
                try
                {
                    _notificationRepository.UpsertNotification(_localDeviceId, json, false);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "保存本地通知到数据库失败");
                }
            }

            _logger.LogDebug("已捕获本地通知: {AppName} - {Title}", appName, title);
            LocalNotificationCaptured?.Invoke();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "处理通知失败");
        }
    }

    private void HandleRemovedNotification(uint id)
    {
        try
        {
            var rawJson = JsonSerializer.Serialize(new
            {
                type = "DATA_NOTIFICATION",
                notificationKey = $"local_{id}",
                notificationType = "Removed",
                appPackage = $"windows_{id}",
                timeStamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString()
            });
            var json = rawJson;
            if (json == null) return;
            _sessionManager.BroadcastMessage(json);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "处理通知移除事件失败");
        }
    }

    private async Task<string?> ExtractAppIconAsync(Windows.UI.Notifications.UserNotification userNotification)
    {
        try
        {
            var bindings = userNotification.Notification?.Visual?.Bindings;
            if (bindings != null)
            {
                foreach (var binding in bindings)
                {
                    try
                    {
                        var getImagesMethod = binding.GetType().GetMethod("GetImages");
                        if (getImagesMethod == null) continue;

                        var images = getImagesMethod.Invoke(binding, null) as IEnumerable;
                        if (images == null) continue;

                        foreach (var image in images)
                        {
                            var href = image.GetType().GetProperty("Href")?.GetValue(image) as string;
                            if (string.IsNullOrEmpty(href)) continue;

                            try
                            {
                                var stream = await ResolveImageUrlAsync(href);
                                if (stream != null)
                                    return await StreamToBase64Async(stream);
                            }
                            catch { }
                        }
                    }
                    catch { }
                }
            }
        }
        catch { }

        try
        {
            var logo = userNotification.AppInfo?.DisplayInfo?.GetLogo(new Windows.Foundation.Size(64, 64));
            if (logo != null)
            {
                var stream = await logo.OpenReadAsync();
                using (stream)
                    return await StreamToBase64Async(stream);
            }
        }
        catch { }

        return null;
    }

    private static async Task<IRandomAccessStream?> ResolveImageUrlAsync(string url)
    {
        try
        {
            var uri = new Uri(url);
            if (uri.Scheme == "file")
            {
                var file = await Windows.Storage.StorageFile.GetFileFromPathAsync(uri.LocalPath);
                return await file.OpenReadAsync();
            }
            if (uri.Scheme is "ms-appx" or "ms-appdata")
            {
                var file = await Windows.Storage.StorageFile.GetFileFromApplicationUriAsync(uri);
                return await file.OpenReadAsync();
            }
        }
        catch { }
        return null;
    }

    private static async Task<string> StreamToBase64Async(IRandomAccessStream stream)
    {
        using var reader = new DataReader(stream.GetInputStreamAt(0));
        var bytes = new byte[stream.Size];
        await reader.LoadAsync((uint)bytes.Length);
        reader.ReadBytes(bytes);
        return Convert.ToBase64String(bytes);
    }

    private static bool IsWorkstationLocked()
    {
        try
        {
            var desktop = NativeMethods.OpenInputDesktop(0, false, 0);
            if (desktop == IntPtr.Zero) return true;
            NativeMethods.CloseDesktop(desktop);
            return false;
        }
        catch { return false; }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Stop();
        GC.SuppressFinalize(this);
    }

    private static class NativeMethods
    {
        [System.Runtime.InteropServices.DllImport("user32.dll")]
        public static extern IntPtr OpenInputDesktop(uint dwFlags, bool fInherit, uint dwDesiredAccess);

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        public static extern bool CloseDesktop(IntPtr hDesktop);
    }
}
