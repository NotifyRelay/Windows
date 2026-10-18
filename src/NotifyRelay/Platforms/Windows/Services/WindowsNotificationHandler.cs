using System.Collections.Concurrent;
using Microsoft.Windows.AppNotifications;
using Microsoft.Windows.AppNotifications.Builder;
using NotifyRelay.Data.AppDatabase.Repository;
using NotifyRelay.Data.Contracts;
using NotifyRelay.Services.Devices;
using NotifyRelay.Services.Media;
using NotifyRelay.Services.Protocol;
using NotifyRelay.Services.Infrastructure;
using NotifyRelay.Utils;
using Windows.System;
using static NotifyRelay.Constants;

namespace NotifyRelay.Platforms.Windows.Services;

using NotifyRelay.Platforms.Windows.Services.Notifications;

/// <summary>
/// Windows implementation of the platform notification handler
/// </summary>
public class WindowsNotificationHandler(ILogger logger, IDeviceManager deviceManager, ILocalNotificationListenerService localListener) : IPlatformNotificationHandler
{
    private readonly ConcurrentDictionary<string, DateTime> _pendingQueue = new();
    private readonly ConcurrentDictionary<string, DateTime> _contentCache = new();

    private readonly RemoteNotificationBuilder _remoteBuilder = new(logger, localListener);
    private readonly TransferNotificationBuilder _transferBuilder = new(logger);

    /// <inheritdoc />
    public async Task ShowRemoteNotification(string payload, string deviceId)
    {
        await _remoteBuilder.ShowRemoteNotification(payload, deviceId, _pendingQueue, _contentCache);
    }

    public async void ShowFileTransferNotification(string subtitle, string fileName, string transferId, uint notificationSequence, double? progress = null)
    {
        _transferBuilder.ShowFileTransferNotification(subtitle, fileName, transferId, notificationSequence, progress);
    }

    /// <inheritdoc />
    public async void ShowCompletedFileTransferNotification(string subtitle, string transferId, string? filePath = null, string? folderPath = null)
    {
        _transferBuilder.ShowCompletedFileTransferNotification(subtitle, transferId, filePath, folderPath);
    }

    /// <inheritdoc />
    public void ShowClipboardNotification(string title, string text, string? iconPath = null)
    {
        _transferBuilder.ShowClipboardNotification(title, text, iconPath);
    }

    /// <inheritdoc />
    public void ShowClipboardNotificationWithActions(string title, string text, string? actionLabel = null, string? actionData = null)
    {
        _transferBuilder.ShowClipboardNotificationWithActions(title, text, actionLabel, actionData);
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
        if (!args.Arguments.ContainsKey("action"))
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
}
