using NotifyRelay.Data.AppDatabase.Repository;
using NotifyRelay.Data.Contracts;
using NotifyRelay.Native;
using NotifyRelay.Services.Filters;
using NotifyRelay.Utils;
using System.Text.Json;
using Windows.ApplicationModel;
using Windows.Storage.Streams;
using Windows.UI.Notifications;
using Windows.UI.Notifications.Management;

namespace NotifyRelay.Services;

public class LocalNotificationListenerService : ILocalNotificationListenerService, IDisposable
{
    private readonly ILogger _logger;
    private readonly ISessionManager _sessionManager;
    private readonly NotificationRepository _notificationRepository;
    private readonly IDeviceManager _deviceManager;
    private readonly RemoteAppRepository _appRepository;

    private string? _localDeviceId;
    public static event Action? LocalNotificationCaptured;

    private UserNotificationListener? _listener;
    private Timer? _pollTimer;
    private readonly HashSet<uint> _knownNotificationIds = [];
    private string? _selfPfn;
    private bool _isPolling;
    private bool _isRunning;
    private bool _disposed;
    private DateTime? _fastPollUntil;

    public bool IsSupported => UserNotificationListener.Current != null;

    public LocalNotificationListenerService(
        ILogger<LocalNotificationListenerService> logger,
        ISessionManager sessionManager,
        NotificationRepository notificationRepository,
        IDeviceManager deviceManager,
        RemoteAppRepository appRepository)
    {
        _logger = logger;
        _sessionManager = sessionManager;
        _notificationRepository = notificationRepository;
        _deviceManager = deviceManager;
        _appRepository = appRepository;
    }

    public void Start()
    {
        if (_isRunning) return;
        if (!IsSupported)
        {
            _logger.LogWarning("当前平台不支持 UserNotificationListener");
            return;
        }
        _ = InitializeAsync();
    }

    private async Task InitializeAsync()
    {
        try
        {
            _listener = UserNotificationListener.Current;
            if (_listener == null)
            {
                _logger.LogWarning("UserNotificationListener.Current 返回 null");
                return;
            }

            try { _selfPfn = Package.Current.Id.FamilyName; }
            catch { _logger.LogWarning("无法获取自身 PackageFamilyName"); }

            var accessStatus = await _listener.RequestAccessAsync();
            if (accessStatus != UserNotificationListenerAccessStatus.Allowed)
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
                TimeSpan.FromSeconds(10));

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

    public void TriggerPoll()
    {
        if (!_isRunning) return;
        _fastPollUntil = DateTime.UtcNow.AddSeconds(20);
        _pollTimer?.Change(TimeSpan.Zero, TimeSpan.FromMilliseconds(500));
    }

    private async Task LoadExistingNotificationsAsync()
    {
        try
        {
            if (_listener == null) return;
            var notifications = await GetNotificationsAsync();
            if (notifications == null) return;

            lock (_knownNotificationIds)
            {
                _knownNotificationIds.Clear();
                foreach (var n in notifications)
                    _knownNotificationIds.Add(n.Id);
            }

            var processed = 0;
            var skipIds = new List<uint>();
            foreach (var notif in notifications)
            {
                if (await ProcessNotificationAsync(notif))
                    processed++;
                else
                    skipIds.Add(notif.Id);
            }

            if (skipIds.Count > 0)
            {
                lock (_knownNotificationIds)
                {
                    foreach (var id in skipIds)
                        _knownNotificationIds.Remove(id);
                }
            }

            _logger.LogDebug("已加载 {Count} 个现有通知, 其中 {Processed} 个新入库", _knownNotificationIds.Count, processed);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "加载现有通知失败");
        }
    }

    private async Task PollNotificationsAsync()
    {
        if (_isPolling) return;
        _isPolling = true;
        try
        {
            if (_listener == null) return;
            var notifications = await GetNotificationsAsync();
            if (notifications == null) return;

            var currentIds = new HashSet<uint>();
            foreach (var notif in notifications)
            {
                currentIds.Add(notif.Id);

                bool isNew;
                lock (_knownNotificationIds) { isNew = _knownNotificationIds.Add(notif.Id); }
                if (isNew)
                {
                    if (!await ProcessNotificationAsync(notif))
                    {
                        lock (_knownNotificationIds) { _knownNotificationIds.Remove(notif.Id); }
                    }
                }
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
        finally
        {
            _isPolling = false;

            if (_fastPollUntil != null && DateTime.UtcNow >= _fastPollUntil.Value)
            {
                _fastPollUntil = null;
                _pollTimer?.Change(TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(10));
            }
        }
    }

    private async Task<IReadOnlyList<UserNotification>?> GetNotificationsAsync()
    {
        if (_listener == null) return null;
        return await _listener.GetNotificationsAsync(NotificationKinds.Toast);
    }

    private async Task<bool> ProcessNotificationAsync(UserNotification userNotification)
    {
        try
        {
            var appInfo = userNotification.AppInfo;
            if (appInfo == null)
            {
                _logger.LogWarning("通知缺少 AppInfo");
                return false;
            }

            if (_selfPfn != null)
            {
                var au = appInfo.AppUserModelId;
                var pfn = appInfo.PackageFamilyName;
                if (au?.StartsWith(_selfPfn, StringComparison.OrdinalIgnoreCase) == true ||
                    string.Equals(pfn, _selfPfn, StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }
            }

            var appPackage = appInfo.AppUserModelId ?? appInfo.PackageFamilyName ?? "unknown";
            var appName = appInfo.DisplayInfo?.DisplayName ?? appPackage;
            var androidPackage = _appRepository.FindPackageByAppName(appName) ?? appPackage;
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
                return false;

            // 内容级去重：用聚合键检查是否已发送过
            var aggregationKey = $"{androidPackage ?? appPackage}|{title}|{text}|New";
            var existing = _notificationRepository.FindByAggregationKey(aggregationKey);
            if (existing != null)
            {
                return false;
            }

            var isLocked = IsWorkstationLocked();
            var appIconBase64 = await ExtractAppIconAsync(userNotification);

            var packageNameValue = androidPackage ?? appName;
            var nowMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

            var rawJson = JsonSerializer.Serialize(new
            {
                type = "DATA_NOTIFICATION",
                notificationKey = $"local_{id}",
                time = nowMs,
                timeStamp = nowMs.ToString(),
                notificationType = "New",
                packageName = packageNameValue,
                appName = appName,
                title = title,
                text = text,
                appIcon = appIconBase64,
                isLocked = isLocked
            });
            if (rawJson == null) return false;
            _sessionManager.BroadcastMessage(rawJson);

            if (_localDeviceId != null)
            {
                try
                {
                    _notificationRepository.UpsertNotification(_localDeviceId, rawJson, false);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "保存本地通知到数据库失败");
                }
            }

            _logger.LogDebug("已捕获本地通知: {AppName} - {Title}", appName, title);
            LocalNotificationCaptured?.Invoke();
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "处理通知失败");
            return false;
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
                packageName = $"windows_{id}",
                time = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                timeStamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString()
            });
            if (rawJson == null) return;
            _sessionManager.BroadcastMessage(rawJson);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "处理通知移除事件失败");
        }
    }

    private async Task<string?> ExtractAppIconAsync(UserNotification userNotification)
    {
        var appInfo = userNotification.AppInfo;

        var candidateNames = new List<string?>();

        var displayName = appInfo?.DisplayInfo?.DisplayName;
        _logger.LogDebug("ExtractAppIcon: DisplayInfo.DisplayName = '{DisplayName}'", displayName);
        candidateNames.Add(displayName);

        var bindings = userNotification.Notification?.Visual?.Bindings;
        if (bindings is { Count: > 0 })
        {
            var textElements = bindings[0].GetTextElements();
            if (textElements.Count > 0)
            {
                var firstText = textElements[0].Text;
                _logger.LogDebug("ExtractAppIcon: 绑定第一个 text 元素 = '{FirstText}'", firstText);
                candidateNames.Add(firstText);
            }
        }

        foreach (var name in candidateNames)
        {
            if (string.IsNullOrEmpty(name))
            {
                _logger.LogDebug("ExtractAppIcon: candidateName 为空，跳过");
                continue;
            }
            _logger.LogDebug("ExtractAppIcon: 尝试用 appName='{Name}' 查包名", name);
            var packageName = _appRepository.FindPackageByAppName(name);
            if (packageName == null)
            {
                _logger.LogDebug("ExtractAppIcon: FindPackageByAppName('{Name}') 返回 null", name);
                continue;
            }
            _logger.LogDebug("ExtractAppIcon: 查到包名 = '{Package}'", packageName);

            var iconPath = IconUtils.GetAppIconFilePath(packageName);
            var exists = IconUtils.AppIconExists(packageName);
            _logger.LogDebug("ExtractAppIcon: 图标路径='{Path}', 存在={Exists}", iconPath, exists);
            if (!exists) continue;

            try
            {
                var file = await Windows.Storage.StorageFile.GetFileFromPathAsync(IconUtils.GetAppIconFilePath(packageName));
                var stream = await file.OpenReadAsync();
                using (stream)
                {
                    var result = await StreamToBase64Async(stream);
                    _logger.LogDebug("ExtractAppIcon: 成功提取图标 base64, 长度={Len}", result?.Length ?? 0);
                    return result;
                }
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "ExtractAppIcon: 读取图标文件失败");
            }
        }

        try
        {
            var logo = appInfo?.DisplayInfo?.GetLogo(new Windows.Foundation.Size(64, 64));
            _logger.LogDebug("ExtractAppIcon: 尝试 Windows app logo, GetLogo = {Logo}", logo != null ? "非空" : "null");
            if (logo != null)
            {
                var stream = await logo.OpenReadAsync();
                using (stream)
                    return await StreamToBase64Async(stream);
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "ExtractAppIcon: GetLogo 失败");
        }

        _logger.LogDebug("ExtractAppIcon: 所有方案均失败，返回 null");
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
