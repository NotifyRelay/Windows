using CommunityToolkit.WinUI;
using NotifyRelay.Data.Contracts;
using NotifyRelay.Data.Models;
using NotifyRelay.Native;
using NotifyRelay.Utils;

namespace NotifyRelay.Services.Notifications;

/// <summary>
/// 应用图标解析服务：负责图标请求/等待、本地图标读取编码、图标响应处理与通知刷新。
/// 通过 Configure 注入通知集合访问器与重建回调，避免反向依赖 NotificationService。
/// </summary>
public class NotificationIconResolver(
    ILogger logger,
    Microsoft.UI.Dispatching.DispatcherQueue dispatcher,
    Func<IRemoteAppService> remoteAppServiceFactory) : INotificationIconResolver
{
    // 跟踪图标请求状态的字典，key: packageName|deviceId, value: TaskCompletionSource<bool>
    private readonly Dictionary<string, TaskCompletionSource<bool>> pendingIconRequests = [];
    private const int ICON_REQUEST_TIMEOUT = 3000; // 图标请求最长等待时间：3秒

    // 反向依赖注入：通知集合访问器 + 重建回调（由 NotificationService 构造后调用 Configure 注入）
    private Func<IEnumerable<Notification>> notificationsProvider = () => [];
    private Action? rebuildCallback;

    public void Configure(Func<IEnumerable<Notification>> notificationsProvider, Action rebuildCallback)
    {
        this.notificationsProvider = notificationsProvider;
        this.rebuildCallback = rebuildCallback;
    }

    /// <summary>
    /// 本地无图标时发送请求并等待响应，最长 ICON_REQUEST_TIMEOUT 毫秒。
    /// 本地已有图标则直接返回，不发请求也不等待。
    /// </summary>
    public async Task WaitForIconAsync(string deviceId, string appPackage)
    {
        // 本地已有图标则无需请求
        if (string.IsNullOrEmpty(appPackage) || IconUtils.AppIconExists(appPackage)) return;

        var requestKey = $"{appPackage}|{deviceId}";
        var iconRequestTcs = new TaskCompletionSource<bool>();
        pendingIconRequests[requestKey] = iconRequestTcs;
        remoteAppServiceFactory().SendIconRequest(deviceId, [appPackage]);

        var timeoutTask = Task.Delay(ICON_REQUEST_TIMEOUT);
        await Task.WhenAny(iconRequestTcs.Task, timeoutTask);
        pendingIconRequests.Remove(requestKey);
    }

    /// <summary>
    /// 读取本地图标文件，一次性返回原始字节（供叠加层弹幕使用）与 data URL（供 TCP 转发使用）；
    /// 本地无图标时两者均为 null。
    /// </summary>
    public Task<(byte[]? Bytes, string? DataUrl)> LoadIconAsync(string appPackage)
    {
        if (string.IsNullOrEmpty(appPackage)) return Task.FromResult<(byte[]?, string?)>((null, null));

        try
        {
            string iconFilePath = IconUtils.GetAppIconFilePath(appPackage);
            if (!System.IO.File.Exists(iconFilePath)) return Task.FromResult<(byte[]?, string?)>((null, null));

            var iconBytes = System.IO.File.ReadAllBytes(iconFilePath);
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
            return Task.FromResult<(byte[]?, string?)>((iconBytes, $"data:{contentType};base64,{b64}"));
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "将图标编码为data URL失败");
            return Task.FromResult<(byte[]?, string?)>((null, null));
        }
    }

    /// <summary>
    /// 刷新指定包名下所有通知的图标，并请求重建分组。
    /// </summary>
    public async Task RefreshNotificationIconsAsync(string packageName)
    {
        try
        {
            await dispatcher.EnqueueAsync(async () =>
            {
                var notificationsToUpdate = notificationsProvider().Where(n => n.AppPackage == packageName).ToList();
                foreach (var notification in notificationsToUpdate)
                {
                    // 更新图标路径和图标
                    notification.IconPath = IconUtils.GetAppIconPath(packageName);
                    await notification.LoadIconAsync();
                }

                // 刷新所有通知
                rebuildCallback?.Invoke();
            });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "刷新通知图标时出错");
        }
    }

    /// <summary>
    /// 解析 DATA_ICON_RESPONSE，落盘图标并刷新相关通知。
    /// </summary>
    public async Task ProcessIconResponseAsync(PairedDevice device, string payload)
    {
        try
        {
            logger.LogInformation("处理ICON_RESPONSE消息");

            // 解析由 Rust 完成：返回 {"icons":[{packageName,iconData}],"missing":[...]}
            var parsed = NativeCore.AppSyncParseIconResponse(payload);
            if (parsed == null) return;

            using var doc = JsonDocument.Parse(parsed);
            var root = doc.RootElement;

            if (root.TryGetProperty("icons", out var iconsArray) && iconsArray.ValueKind == JsonValueKind.Array)
            {
                logger.LogInformation("接收到图标响应，包含 {count} 个图标", iconsArray.GetArrayLength());
                int savedCount = 0;
                foreach (var iconElement in iconsArray.EnumerateArray())
                {
                    // 获取包名
                    if (!iconElement.TryGetProperty("packageName", out var packageProp))
                    {
                        logger.LogWarning("图标响应中的图标缺少 packageName 属性");
                        continue;
                    }

                    var packageName = packageProp.GetString();
                    if (string.IsNullOrEmpty(packageName))
                    {
                        logger.LogWarning("图标响应中的图标 packageName 为空");
                        continue;
                    }

                    // 获取图标数据
                    if (!iconElement.TryGetProperty("iconData", out var iconDataProp))
                    {
                        logger.LogWarning("图标响应中的图标缺少 iconData 属性");
                        continue;
                    }

                    var iconData = iconDataProp.GetString();
                    if (string.IsNullOrEmpty(iconData))
                    {
                        logger.LogWarning("图标响应中的图标 iconData 为空");
                        continue;
                    }

                    logger.LogInformation("正在保存应用 {packageName} 的图标，数据长度：{length}", packageName, iconData.Length);
                    // 保存图标
                    await IconUtils.SaveAppIconToPathAsync(iconData, packageName);
                    savedCount++;

                    // 完成等待的图标请求任务
                    var requestKey = $"{packageName}|{device.Id}";
                    if (pendingIconRequests.TryGetValue(requestKey, out var tcs))
                    {
                        tcs.TrySetResult(true);
                        logger.LogDebug("已通知图标请求完成：{PackageName}", packageName);
                    }

                    // 触发应用图标更新
                    await RefreshNotificationIconsAsync(packageName);
                }
                logger.LogInformation("图标响应处理完成，已保存 {savedCount} 个应用图标", savedCount);
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
}
