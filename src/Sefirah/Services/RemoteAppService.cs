using NotifyRelay.Data.AppDatabase.Repository;
using NotifyRelay.Data.Contracts;
using NotifyRelay.Data.Models;
using NotifyRelay.Utils;

namespace NotifyRelay.Services;

public class RemoteAppService(
    ILogger<RemoteAppService> logger,
    RemoteAppRepository remoteAppRepository,
    IDeviceManager deviceManager) : IRemoteAppService
{
    public async Task ProcessAppListResponseAsync(PairedDevice device, string payload)
    {
        try
        {
            if (!payload.TrimStart().StartsWith('{') && !payload.TrimStart().StartsWith('['))
            {
                logger.LogWarning("跳过非 JSON 应用列表响应：{payload}", payload.Length > 50 ? payload[..50] + "..." : payload);
                return;
            }

            // 首先尝试解析JSON
            using var doc = JsonDocument.Parse(payload);
            var root = doc.RootElement;

            logger.LogDebug("处理APP_LIST_RESPONSE消息");

            var appList = new ApplicationList { AppList = new List<ApplicationInfoMessage>() };

            if (root.TryGetProperty("apps", out var appsArray))
            {
                foreach (var appElement in appsArray.EnumerateArray())
                {
                    if (appElement.TryGetProperty("packageName", out var pkgNameProp))
                    {
                        var packageName = pkgNameProp.GetString();
                        if (!string.IsNullOrEmpty(packageName))
                        {
                            var appName = appElement.TryGetProperty("appName", out var appNameProp) ? appNameProp.GetString() ?? packageName : packageName;
                            var appInfo = new ApplicationInfoMessage { PackageName = packageName, AppName = appName };
                            appList.AppList.Add(appInfo);
                        }
                    }
                }

                remoteAppRepository.UpdateApplicationList(device, appList);
                logger.LogDebug("已更新应用列表，共 {Count} 个应用", appList.AppList.Count);

                // 收集所有没有图标的应用的包名
                var packageNamesWithoutIcons = new List<string>();
                foreach (var appInfo in appList.AppList)
                {
                    if (!IconUtils.AppIconExists(appInfo.PackageName))
                    {
                        packageNamesWithoutIcons.Add(appInfo.PackageName);
                    }
                }

                // 发送批量图标请求
                if (packageNamesWithoutIcons.Count > 0)
                {
                    logger.LogDebug("发送 {Count} 个图标请求", packageNamesWithoutIcons.Count);
                    SendIconRequest(device.Id, packageNamesWithoutIcons);
                }
            }
        }
        catch (JsonException ex)
        {
            logger.LogWarning("解析应用列表响应JSON时出错：{ex.Message}", ex.Message);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "处理应用列表响应时出错");
        }
    }

    /// <summary>
    /// 发送应用列表请求
    /// </summary>
    /// <param name="deviceId">设备 ID</param>
    public void SendAppListRequest(string deviceId)
    {
        // 构建应用列表请求对象
        var request = new ApplicationListRequest();

        // 序列化为 JSON
        string requestJson = JsonSerializer.Serialize(request);

        // 调用通用发送方法
        _ = ProtocolSender.SendMessageAsync(logger, deviceManager, deviceId, requestJson);
    }

    /// <summary>
    /// 发送图标请求
    /// </summary>
    /// <param name="deviceId">设备 ID</param>
    /// <param name="packageNames">应用包名列表</param>
    public void SendIconRequest(string deviceId, List<string> packageNames)
    {
        logger.LogInformation("开始发送图标请求：deviceId={deviceId}, packageCount={packageCount}", deviceId, packageNames.Count);

        // 构建图标请求对象（支持单个或多个包名）
        var request = new IconRequest();
        if (packageNames.Count == 1)
        {
            request.PackageName = packageNames.First();
        }
        else
        {
            request.PackageNames = packageNames;
        }

        // 序列化为 JSON
        string requestJson = JsonSerializer.Serialize(request);

        // 调用通用发送方法
        _ = ProtocolSender.SendMessageAsync(logger, deviceManager, deviceId, requestJson);
    }
}
