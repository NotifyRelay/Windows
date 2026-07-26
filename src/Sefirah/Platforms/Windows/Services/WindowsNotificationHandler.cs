using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using Microsoft.Windows.AppNotifications;
using Microsoft.Windows.AppNotifications.Builder;
using NotifyRelay.Data.AppDatabase.Repository;
using NotifyRelay.Data.Contracts;
using NotifyRelay.Data.Models;
using NotifyRelay.Extensions;
using NotifyRelay.Services;
using NotifyRelay.Utils;
using Windows.System;
using static NotifyRelay.Constants;

namespace NotifyRelay.Platforms.Windows.Services;

/// <summary>
/// Windows implementation of the platform notification handler
/// </summary>
public class WindowsNotificationHandler(ILogger logger, ISessionManager sessionManager, IDeviceManager deviceManager, ILocalNotificationListenerService localListener, NotificationRepository notificationRepo) : IPlatformNotificationHandler
{
    private static readonly TimeSpan TempIconMaxAge = TimeSpan.FromDays(1); // 清理 1 天以前的临时图标
    private const string TempIconsFolderName = "Sefirah-pc-icons";
    private static readonly TimeSpan ContentCacheTtl = TimeSpan.FromSeconds(10);
    private readonly ConcurrentDictionary<string, DateTime> _pendingQueue = new();
    private readonly ConcurrentDictionary<string, DateTime> _contentCache = new();

    private static string GetTempIconsDirectory()
    {
        string tempPath = Path.GetTempPath();
        string tempIconsDirectory = Path.Combine(tempPath, TempIconsFolderName);
        try
        {
            Directory.CreateDirectory(tempIconsDirectory);

            // 清理超过阈值的旧文件
            try
            {
                var files = Directory.GetFiles(tempIconsDirectory);
                var expireBefore = DateTime.UtcNow - TempIconMaxAge;
                foreach (var f in files)
                {
                    try
                    {
                        var info = new FileInfo(f);
                        if (info.Exists && info.LastWriteTimeUtc < expireBefore)
                        {
                            info.Delete();
                        }
                    }
                    catch
                    {
                        // 忽略单个文件删除错误
                    }
                }
            }
            catch
            {
                // 忽略清理错误
            }
        }
        catch
        {
            // ignore
        }

        return tempIconsDirectory;
    }

    /// <inheritdoc />
    public async Task ShowRemoteNotification(string payload, string deviceId)
    {
        try
        {
            using var doc = JsonDocument.Parse(payload);
            var root = doc.RootElement;
            var appName = root.TryGetProperty("appName", out var anProp) ? anProp.GetString() : null;
            var title = root.TryGetProperty("title", out var tProp) ? tProp.GetString() : null;
            var text = root.TryGetProperty("text", out var txProp) ? txProp.GetString() : null;
            var tag = root.TryGetProperty("tag", out var tgProp) ? tgProp.GetString() : null;
            var groupKey = root.TryGetProperty("groupKey", out var gkProp) ? gkProp.GetString() : null;
            var appPackage = root.TryGetProperty("packageName", out var pnProp) && pnProp.ValueKind == JsonValueKind.String ? pnProp.GetString() : null;
            var largeIcon = root.TryGetProperty("largeIcon", out var liProp) ? liProp.GetString() : null;

            var builder = new AppNotificationBuilder()
                .AddText(appName, new AppNotificationTextProperties().SetMaxLines(1))
                .AddText(title)
                .AddText(text)
                .SetTag(tag ?? string.Empty)
                .SetGroup(groupKey ?? string.Empty);

            if (!string.IsNullOrEmpty(appPackage))
            {
                var iconUri = await IconUtils.GetAppIconUriAsync(appPackage);
                var appIconExists = IconUtils.AppIconExists(appPackage);


                if (iconUri is not null)
                {
                    try
                    {
                        if (iconUri.Scheme.Equals("ms-appdata", StringComparison.OrdinalIgnoreCase) || iconUri.Scheme.Equals("file", StringComparison.OrdinalIgnoreCase))
                        {
                            try
                            {
                                var storageFile = await StorageFile.GetFileFromApplicationUriAsync(iconUri);

                                string tempIconsDirectory = GetTempIconsDirectory();

                                string tempFileName = $"{appPackage}_{DateTime.UtcNow.Ticks}.png";
                                string tempFilePath = Path.Combine(tempIconsDirectory, tempFileName);

                                // 复制图标文件到临时目录
                                var destFolder = await StorageFolder.GetFolderFromPathAsync(tempIconsDirectory);
                                await storageFile.CopyAsync(destFolder, tempFileName, NameCollisionOption.ReplaceExisting);

                                // 使用 file:// URI 引用临时图标文件
                                var fileUri = new Uri($"file://{tempFilePath}");

                                builder.SetAppLogoOverride(fileUri, AppNotificationImageCrop.Circle);
                            }
                            catch (COMException comExLocal)
                            {
                                logger.LogDebug(comExLocal, "WinRT COM异常：无法读取本地图标 URI，回退使用原始 URI：{IconUri}", iconUri);
                                builder.SetAppLogoOverride(iconUri, AppNotificationImageCrop.Circle);
                            }
                            catch (Exception exLocal)
                            {
                                logger.LogWarning(exLocal, "无法读取本地图标 URI，回退使用原始 URI：{IconUri}", iconUri);
                                builder.SetAppLogoOverride(iconUri, AppNotificationImageCrop.Circle);
                            }
                        }
                        else
                        {
                            logger.LogDebug("设置通知图标为 {IconUri}", iconUri);
                            builder.SetAppLogoOverride(iconUri, AppNotificationImageCrop.Circle);
                        }
                    }
                    catch (COMException comEx)
                    {
                        logger.LogDebug(comEx, "WinRT COM异常：设置通知图标时出错");
                    }
                    catch (Exception ex)
                    {
                        logger.LogWarning(ex, "设置通知图标时出错");
                    }
                    }
                else
                {
                    var notificationKey = root.TryGetProperty("notificationKey", out var nkProp) ? nkProp.GetString() : null;
                    if (!string.IsNullOrEmpty(largeIcon))
                    {
                        try
                        {
                            string tempIconsDirectory = GetTempIconsDirectory();

                            string tempFileName = $"largeIcon_{DateTime.UtcNow.Ticks}.png";
                            string tempFilePath = Path.Combine(tempIconsDirectory, tempFileName);

                            var bytes = Convert.FromBase64String(largeIcon);
                            await File.WriteAllBytesAsync(tempFilePath, bytes);

                            var fileUri = new Uri($"file://{tempFilePath}");
                            logger.LogDebug("包名图标不存在，已保存大图标到临时目录：{FileUri}，通知键：{NotificationKey}", fileUri, notificationKey);
                            builder.SetAppLogoOverride(fileUri, AppNotificationImageCrop.Circle);
                        }
                        catch (COMException comEx)
                        {
                            logger.LogDebug(comEx, "WinRT COM异常：保存大图标到临时目录时出错，通知键：{NotificationKey}", notificationKey);
                        }
                        catch (Exception ex)
                        {
                            logger.LogWarning(ex, "保存大图标到临时目录时出错，通知键：{NotificationKey}", notificationKey);
                        }
                    }
                    else
                    {
                        logger.LogDebug("未找到应用图标或大图标，通知键：{NotificationKey}，包名：{AppPackage}", notificationKey, appPackage);
                    }
                }
            }
            else if (!string.IsNullOrEmpty(largeIcon))
            {
                var notificationKey = root.TryGetProperty("notificationKey", out var nkProp) ? nkProp.GetString() : null;
                try
                {
                    string tempIconsDirectory = GetTempIconsDirectory();

                    string tempFileName = $"largeIcon_{notificationKey}_{DateTime.UtcNow.Ticks}.png";
                    string tempFilePath = Path.Combine(tempIconsDirectory, tempFileName);

                    var bytes = Convert.FromBase64String(largeIcon);
                    await File.WriteAllBytesAsync(tempFilePath, bytes);

                    var fileUri = new Uri($"file://{tempFilePath}");
                    logger.LogDebug("未设置包名，已保存大图标到临时目录：{FileUri}，通知键：{NotificationKey}", fileUri, notificationKey);
                    builder.SetAppLogoOverride(fileUri, AppNotificationImageCrop.Circle);
                }
                catch (COMException comEx)
                {
                    logger.LogDebug(comEx, "WinRT COM异常：保存大图标到临时目录时出错，通知键：{NotificationKey}", notificationKey);
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "保存大图标到临时目录时出错，通知键：{NotificationKey}", notificationKey);
                }
            }
            else
            {
                logger.LogDebug("未设置图标：LargeIcon 为空");
            }

            var notification = builder.BuildNotification();
            notification.ExpiresOnReboot = true;

            var titleText = title ?? "";
            var textText = text ?? "";
            var aggregationKey = $"{titleText}|{textText}|New";

            if (!_pendingQueue.TryAdd(aggregationKey, DateTime.UtcNow))
            {
                logger.LogDebug("待复刻队列中已有相同通知，跳过 (key={Key})", aggregationKey);
                return;
            }

            await Task.Delay(1000);

            try
            {
                if (_contentCache.ContainsKey(aggregationKey))
                {
                    logger.LogDebug("10s 内容缓存命中，取消复刻 (key={Key})", aggregationKey);
                    return;
                }

                AppNotificationManager.Default.Show(notification);

                _contentCache.TryAdd(aggregationKey, DateTime.UtcNow);
                CleanExpiredCacheEntries();
            }
            finally
            {
                _pendingQueue.TryRemove(aggregationKey, out _);
            }

            localListener.TriggerPoll();
        }
        catch (COMException comEx)
        {
            logger.LogDebug(comEx, "WinRT COM异常：显示远程通知失败");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "显示远程通知失败");
        }
    }

    public async void ShowFileTransferNotification(string subtitle, string fileName, string transferId, uint notificationSequence, double? progress = null)
    {
        try
        {
            // if transfer is in progress, update existing notification
            if (progress.HasValue && progress > 0 && progress < 100)
            {
                var progressData = new AppNotificationProgressData(notificationSequence)
                {
                    Title = fileName,
                    Value = progress.Value / 100,
                    ValueStringOverride = $"{progress.Value:F0}%",
                    Status = subtitle
                };
                await AppNotificationManager.Default.UpdateAsync(progressData, transferId, Constants.Notification.FileTransferGroup);
            }
            else
            {
                var builder = new AppNotificationBuilder()
                    .AddText("FileTransferNotification.Title".GetLocalizedResource())
                    .SetTag(transferId)
                    .SetGroup(Constants.Notification.FileTransferGroup)
                    .MuteAudio()
                    .AddButton(new AppNotificationButton("FileTransferNotificationAction.Cancel".GetLocalizedResource())
                        .AddArgument("notificationType", ToastNotificationType.FileTransfer)
                        .AddArgument("action", "cancel"))
                    .AddProgressBar(new AppNotificationProgressBar()
                        .BindTitle()
                        .BindValue()
                        .BindValueStringOverride()
                        .BindStatus());

                var notification = builder.BuildNotification();
                notification.ExpiresOnReboot = true;

                // Set initial progress data
                notification.Progress = new AppNotificationProgressData(notificationSequence)
                {
                    Title = fileName,
                    Value = 0,
                    ValueStringOverride = "0%",
                    Status = subtitle
                };

                AppNotificationManager.Default.Show(notification);
            }
        }
        catch (COMException comEx)
        {
            logger.LogDebug(comEx, "WinRT COM异常：文件传输通知失败，进度：{Progress}, 序列：{NotificationSequence}", progress, notificationSequence);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, $"通知失败，进度：{progress}, 序列：{notificationSequence}");
        }
    }


    /// <inheritdoc />
    public async void ShowCompletedFileTransferNotification(string subtitle, string transferId, string? filePath = null, string? folderPath = null)
    {
        // TODO: show hero image if available   
        try
        {
            await Task.Delay(500);
            var builder = new AppNotificationBuilder()
                .AddText("FileTransferNotification.Completed".GetLocalizedResource())
                .AddText(subtitle)
                .SetTag(transferId)
                .SetGroup(Constants.Notification.FileTransferGroup);

            if (!string.IsNullOrEmpty(filePath))
            {
                builder.AddButton(new AppNotificationButton("FileTransferNotificationAction.OpenFile".GetLocalizedResource())
                    .AddArgument("notificationType", ToastNotificationType.FileTransfer)
                    .AddArgument("action", "openFile")
                    .AddArgument("filePath", filePath));
            }

            if (!string.IsNullOrEmpty(folderPath))
            {
                builder.AddButton(new AppNotificationButton("FileTransferNotificationAction.OpenFolder".GetLocalizedResource())
                    .AddArgument("notificationType", ToastNotificationType.FileTransfer)
                    .AddArgument("action", "openFolder")
                    .AddArgument("folderPath", folderPath));
            }

            var notification = builder.BuildNotification();
            notification.ExpiresOnReboot = true;
            AppNotificationManager.Default.Show(notification);
        }
        catch (COMException comEx)
        {
            logger.LogDebug(comEx, "WinRT COM异常：显示文件传输通知失败");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "显示文件传输通知失败");
        }
    }


    /// <inheritdoc />
    public void ShowClipboardNotification(string title, string text, string? iconPath = null)
    {
        try
        {
            var builder = new AppNotificationBuilder()
                .AddText(title)
                .AddText(text)
                .SetTag($"clipboard_{DateTime.Now.Ticks}")
                .SetGroup("clipboard");

            var notification = builder.BuildNotification();
            notification.ExpiresOnReboot = true;
            AppNotificationManager.Default.Show(notification);
        }
        catch (COMException comEx)
        {
            logger.LogDebug(comEx, "WinRT COM异常：显示剪贴板通知失败");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "显示简单通知失败");
        }
    }

    /// <inheritdoc />
    public void ShowClipboardNotificationWithActions(string title, string text, string? actionLabel = null, string? actionData = null)
    {
        try
        {
            var builder = new AppNotificationBuilder()
                .AddText(title)
                .AddText(text)
                .SetTag($"clipboard_{DateTime.Now.Ticks}")
                .SetGroup("clipboard");

            if (!string.IsNullOrEmpty(actionLabel) && !string.IsNullOrEmpty(actionData))
            {
                builder.AddButton(new AppNotificationButton(actionLabel)
                    .AddArgument("notificationType", ToastNotificationType.Clipboard)
                    .AddArgument("uri", actionData));
            }

            var notification = builder.BuildNotification();
            notification.ExpiresOnReboot = true;
            AppNotificationManager.Default.Show(notification);
        }
        catch (COMException comEx)
        {
            logger.LogDebug(comEx, "WinRT COM异常：显示带操作的剪贴板通知失败");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "显示剪贴板通知失败");
        }
    }

    /// <inheritdoc />
    public async Task RegisterForNotifications()
    {
        AppNotificationManager.Default.NotificationInvoked -= OnNotificationInvoked;
        AppNotificationManager.Default.NotificationInvoked += OnNotificationInvoked;

        try
        {
            await Task.Run(() => AppNotificationManager.Default.Register());
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "无法注册通知，继续不显示通知");
        }
    }

    private void OnNotificationInvoked(AppNotificationManager sender, AppNotificationActivatedEventArgs args)
    {
        try
        {
            logger.LogInformation("通知被触发 - 参数：{Arguments}", string.Join(", ", args.Arguments.Select(x => $"{x.Key}={x.Value}")));

            if (!args.Arguments.TryGetValue("notificationType", out var notificationType)) return;

            switch (notificationType)
            {
                case ToastNotificationType.FileTransfer:
                    HandleFileTransferNotification(args);
                    break;

                case ToastNotificationType.RemoteNotification:
                    HandleMessageNotification(args);
                    break;

                case ToastNotificationType.Clipboard:
                    HandleClipboardNotification(args);
                    break;

                default:
                    logger.LogWarning("未处理的通知类型：{NotificationType}", notificationType);
                    break;
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "处理通知操作时出错");
        }
    }

    private static async void HandleClipboardNotification(AppNotificationActivatedEventArgs args)
    {
        if (args.Arguments.TryGetValue("uri", out var uriString) && Uri.TryCreate(uriString, UriKind.Absolute, out Uri? uri) && ClipboardService.IsValidWebUrl(uri))
        {
            await Launcher.LaunchUriAsync(uri);
        }
    }

    private static void HandleFileTransferNotification(AppNotificationActivatedEventArgs args)
    {
        if (args.Arguments.TryGetValue("action", out string? action))
        {
            switch (action)
            {
                case "openFile":
                    if (args.Arguments.TryGetValue("filePath", out string? filePath) && File.Exists(filePath))
                    {
                        Process.Start(new ProcessStartInfo
                        {
                            FileName = filePath,
                            UseShellExecute = true
                        });
                    }
                    break;
                case "openFolder":
                    if (args.Arguments.TryGetValue("folderPath", out string? folderPath) && Directory.Exists(folderPath))
                    {
                        Process.Start(new ProcessStartInfo
                        {
                            FileName = "explorer.exe",
                            Arguments = $"\"{folderPath}\"",
                            UseShellExecute = true
                        });
                    }
                    break;
                case "cancel":
                    var fileTransferService = Ioc.Default.GetRequiredService<IFileTransferService>();
                    fileTransferService.CancelTransfer();
                    break;
            }
        }
    }

    private void HandleMessageNotification(AppNotificationActivatedEventArgs args)
    {
        if (!args.Arguments.TryGetValue("action", out var actionType))
            return;

        if (!args.Arguments.TryGetValue("deviceId", out var deviceId))
            return;

        var device = deviceManager.FindDeviceById(deviceId);
        if (device is null) return;

    }

    /// <inheritdoc />
    public async Task RemoveNotificationByTag(string? tag)
    {
        if (string.IsNullOrEmpty(tag)) return;
        await AppNotificationManager.Default.RemoveByTagAsync(tag);
    }

    /// <inheritdoc />
    public async Task RemoveNotificationsByGroup(string? groupKey)
    {
        if (string.IsNullOrEmpty(groupKey)) return;
        await AppNotificationManager.Default.RemoveByGroupAsync(groupKey);
    }

    public async Task RemoveNotificationsByTagAndGroup(string? tag, string? groupKey)
    {
        if (string.IsNullOrEmpty(tag) || string.IsNullOrEmpty(groupKey)) return;
        await AppNotificationManager.Default.RemoveByTagAndGroupAsync(tag, groupKey);
    }

    /// <inheritdoc />
    public async Task ClearAllNotifications()
    {
        await AppNotificationManager.Default.RemoveAllAsync();
    }

    private void CleanExpiredCacheEntries()
    {
        var cutoff = DateTime.UtcNow - ContentCacheTtl;
        foreach (var kvp in _contentCache)
        {
            if (kvp.Value < cutoff)
                _contentCache.TryRemove(kvp.Key, out _);
        }
    }
}
