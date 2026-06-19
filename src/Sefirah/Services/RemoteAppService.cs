using NotifyRelay.Data.AppDatabase.Repository;
using NotifyRelay.Data.Contracts;
using NotifyRelay.Data.Models;
using NotifyRelay.Utils;

namespace NotifyRelay.Services;

public class RemoteAppService(
    ILogger<RemoteAppService> logger,
    RemoteAppRepository remoteAppRepository,
    IDeviceManager deviceManager,
    IProtocolSender protocolSender) : IRemoteAppService
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

                var packageNamesWithoutIcons = new List<string>();
                foreach (var appInfo in appList.AppList)
                {
                    if (!IconUtils.AppIconExists(appInfo.PackageName))
                    {
                        packageNamesWithoutIcons.Add(appInfo.PackageName);
                    }
                }

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

    public void SendAppListRequest(string deviceId)
    {
        var request = new ApplicationListRequest();
        string requestJson = JsonSerializer.Serialize(request);
        _ = protocolSender.SendMessageAsync(deviceId, requestJson);
    }

    public void SendIconRequest(string deviceId, List<string> packageNames)
    {
        logger.LogInformation("开始发送图标请求：deviceId={deviceId}, packageCount={packageCount}", deviceId, packageNames.Count);

        var request = new IconRequest();
        if (packageNames.Count == 1)
        {
            request.PackageName = packageNames.First();
        }
        else
        {
            request.PackageNames = packageNames;
        }

        string requestJson = JsonSerializer.Serialize(request);
        _ = protocolSender.SendMessageAsync(deviceId, requestJson);
    }
}
