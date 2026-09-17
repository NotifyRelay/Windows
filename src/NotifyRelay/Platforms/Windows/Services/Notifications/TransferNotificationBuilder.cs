using Microsoft.Windows.AppNotifications;
using Microsoft.Windows.AppNotifications.Builder;
using System.Runtime.InteropServices;
using static NotifyRelay.Constants;

namespace NotifyRelay.Platforms.Windows.Services.Notifications;

/// <summary>
/// 负责文件传输通知与剪贴板通知的构建与发送（不处理激活回调）。
/// </summary>
internal sealed class TransferNotificationBuilder
{
    private readonly ILogger _logger;

    public TransferNotificationBuilder(ILogger logger)
    {
        _logger = logger;
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
            _logger.LogDebug(comEx, "WinRT COM异常：文件传输通知失败，进度：{Progress}, 序列：{NotificationSequence}", progress, notificationSequence);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"通知失败，进度：{progress}, 序列：{notificationSequence}");
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
            _logger.LogDebug(comEx, "WinRT COM异常：显示文件传输通知失败");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "显示文件传输通知失败");
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
            _logger.LogDebug(comEx, "WinRT COM异常：显示剪贴板通知失败");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "显示简单通知失败");
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
            _logger.LogDebug(comEx, "WinRT COM异常：显示带操作的剪贴板通知失败");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "显示剪贴板通知失败");
        }
    }
}
