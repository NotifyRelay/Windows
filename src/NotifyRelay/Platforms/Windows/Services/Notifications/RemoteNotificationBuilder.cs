using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using Microsoft.Windows.AppNotifications;
using Microsoft.Windows.AppNotifications.Builder;
using NotifyRelay.Data.Contracts;
using NotifyRelay.Services.Devices;

namespace NotifyRelay.Platforms.Windows.Services.Notifications;

/// <summary>
/// 负责远程通知 payload 解析、去重判定、通知构建与发送。
/// 去重缓存（_pendingQueue / _contentCache）的所有权仍由主类持有，通过引用传入。
/// </summary>
internal sealed class RemoteNotificationBuilder
{
    private static readonly TimeSpan ContentCacheTtl = TimeSpan.FromSeconds(10);

    private readonly ILogger _logger;
    private readonly ILocalNotificationListenerService _localListener;

    public RemoteNotificationBuilder(ILogger logger, ILocalNotificationListenerService localListener)
    {
        _logger = logger;
        _localListener = localListener;
    }

    public async Task ShowRemoteNotification(
        string payload,
        string deviceId,
        ConcurrentDictionary<string, DateTime> pendingQueue,
        ConcurrentDictionary<string, DateTime> contentCache)
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

            await NotificationIconProvider.ResolveIconAsync(builder, root, appPackage, largeIcon, _logger);

            var notification = builder.BuildNotification();
            notification.ExpiresOnReboot = true;

            var titleText = title ?? "";
            var textText = text ?? "";
            var aggregationKey = $"{titleText}|{textText}|New";

            if (!pendingQueue.TryAdd(aggregationKey, DateTime.UtcNow))
            {
                _logger.LogDebug("待复刻队列中已有相同通知，跳过 (key={Key})", aggregationKey);
                return;
            }

            await Task.Delay(1000);

            try
            {
                if (contentCache.ContainsKey(aggregationKey))
                {
                    _logger.LogDebug("10s 内容缓存命中，取消复刻 (key={Key})", aggregationKey);
                    return;
                }

                AppNotificationManager.Default.Show(notification);

                contentCache.TryAdd(aggregationKey, DateTime.UtcNow);
                CleanExpiredCacheEntries(contentCache);
            }
            finally
            {
                pendingQueue.TryRemove(aggregationKey, out _);
            }

            _localListener.TriggerPoll();
        }
        catch (COMException comEx)
        {
            _logger.LogDebug(comEx, "WinRT COM异常：显示远程通知失败");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "显示远程通知失败");
        }
    }

    private static void CleanExpiredCacheEntries(ConcurrentDictionary<string, DateTime> contentCache)
    {
        var cutoff = DateTime.UtcNow - ContentCacheTtl;
        foreach (var kvp in contentCache)
        {
            if (kvp.Value < cutoff)
                contentCache.TryRemove(kvp.Key, out _);
        }
    }
}
