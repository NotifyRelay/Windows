using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using NotifyRelay.Data.AppDatabase.Repository;
using NotifyRelay.Data.Contracts;
using NotifyRelay.Data.Enums;
using NotifyRelay.Data.Models;
using NotifyRelay.Helpers;
using NotifyRelay.Utils;
using NotifyRelay.Utils.Serialization;
#if WINDOWS
using NotifyRelay.Platforms.Windows.Services;
#endif

namespace NotifyRelay.Services;

/// <summary>
/// 统一协议路由器
/// 
/// 职责：
/// - 统一解析 TCP 文本首行中的 DATA_* 报文头
/// - 统一做认证检查与解密
/// - 将明文负载转发给对应的功能模块（通知/图标/应用列表等）
/// </summary>
public class ProtocolRouter
{
    private const string DeviceTypeAndroid = "android";
    private readonly Lazy<IMessageHandler> messageHandler;
    private readonly ILogger<ProtocolRouter> logger;
    private readonly IDeviceManager deviceManager;
    private readonly IScreenMirrorService screenMirrorService;
#if WINDOWS
    private readonly Lazy<NetworkDriveMapper> networkDriveMapper;
#endif

    public ProtocolRouter(
        Func<IMessageHandler> messageHandlerFactory,
        ILogger<ProtocolRouter> logger,
        IDeviceManager deviceManager,
        IScreenMirrorService screenMirrorService
#if WINDOWS
        , Func<NetworkDriveMapper> networkDriveMapperFactory
#endif
        )
    {
        this.messageHandler = new Lazy<IMessageHandler>(messageHandlerFactory);
        this.logger = logger;
        this.deviceManager = deviceManager;
        this.screenMirrorService = screenMirrorService;
#if WINDOWS
        if (networkDriveMapperFactory == null)
        {
            throw new ArgumentNullException(nameof(networkDriveMapperFactory), "NetworkDriveMapperFactory cannot be null on Windows platform");
        }
        this.networkDriveMapper = new Lazy<NetworkDriveMapper>(networkDriveMapperFactory);
#endif
    }

    private static bool IsRemoteDeviceAndroid(PairedDevice? device)
    {
        // 允许RemoteDeviceType为null的情况，因为有些Android设备可能没有在握手时正确设置此属性
        return device != null && (device.RemoteDeviceType?.Equals(DeviceTypeAndroid, StringComparison.OrdinalIgnoreCase) ?? true);
    }

    /// <summary>
    /// 处理DATA_*加密业务消息
    /// </summary>
    /// <param name="device">设备</param>
    /// <param name="message">完整消息</param>
    public async Task ProcessDataMessageAsync(PairedDevice device, string message)
    {
        try
        {
            var parts = message.Split(':');
            if (parts.Length < 4)
            {
                logger.LogWarning("无效的 DATA 帧格式: {message}", message.Length > 50 ? message[..50] + "..." : message);
                return;
            }

            if (device.SharedSecret is null)
            {
                logger.LogWarning("设备 {id} 缺少共享密钥，无法处理加密消息", device.Id);
                return;
            }

            var messageType = parts[0];
            var encryptedPayload = string.Join(":", parts.Skip(3));
            var decryptedPayload = NotifyCryptoHelper.Decrypt(encryptedPayload, device.SharedSecret);
            // 根据具体的DATA_*报文头进行分流处理
            switch (messageType)
            {
                case "DATA_APP_LIST_REQUEST":
                    // 应用列表请求
                    await HandleAppListRequestAsync(device, decryptedPayload);
                    break;
                    
                case "DATA_ICON_REQUEST":
                    // 图标请求
                    await HandleIconRequestAsync(device, decryptedPayload);
                    break;
                    
                case "DATA_NOTIFICATION":
                    // 普通通知
                    await HandleNotificationPayloadAsync(device, decryptedPayload);
                    break;
                    
                case "DATA_MEDIAPLAY":
                    // 媒体播放信息，直接调用媒体播放通知处理
                    await HandleMediaPlayAsync(device, decryptedPayload);
                    break;
                    
                case "DATA_APP_LIST_RESPONSE":
                    // 应用列表响应
                    await HandleAppListResponseAsync(device, decryptedPayload);
                    break;
                    
                case "DATA_ICON_RESPONSE":
                    // 图标响应
                    await HandleIconResponseAsync(device, decryptedPayload);
                    break;
                    
                case "DATA_AUDIO_REQUEST":
                    // 音频请求
                    await HandleNotificationPayloadAsync(device, decryptedPayload);
                    break;
                    
                case "DATA_SUPERISLAND":
                    // 超级岛通知，直接忽略
                    break;
                    
                case "DATA_MEDIA_CONTROL":
                    // 媒体控制指令，委托给专门的处理方法
                    logger.LogDebug("处理DATA_MEDIA_CONTROL消息，内容: {decryptedPayload}", decryptedPayload);
                    await HandleMediaControlMessageAsync(device, decryptedPayload);
                    break;
                
#if WINDOWS
                case "DATA_FTP":
                    // ftp 消息，直接处理网络磁盘映射
                    logger.LogDebug("处理DATA_FTP消息，内容: {decryptedPayload}", decryptedPayload);
                    try
                    {
                        var doc = JsonDocument.Parse(decryptedPayload);
                        var root = doc.RootElement;
                        
                        var action = root.TryGetProperty("action", out var actionProp) ? actionProp.GetString() : string.Empty;
                        logger.LogDebug("ftp消息action: {action}", action);
                        
                        if (action == "started")
                        {
                            // ftp服务已启动，解析服务器信息
                            if (root.TryGetProperty("ipAddress", out var ipAddressProp))
                            {
                                var ipAddress = ipAddressProp.GetString();
                                var port = root.TryGetProperty("port", out var portProp) ? portProp.GetInt32() : 22;
                                
                                if (!string.IsNullOrEmpty(ipAddress))
                                {
                                    var serverInfo = new ftpServerInfo
                                    {
                                        IpAddress = ipAddress,
                                        Port = port
                                    };
                                    
                                    // 直接调用NetworkDriveMapper进行网络磁盘映射
                                    try
                                    {
                                        string mappedDrive = networkDriveMapper.Value.MapftpDrive(device, serverInfo);
                                        if (!string.IsNullOrEmpty(mappedDrive))
                                        {
                                            logger.LogDebug("设备 {DeviceName} 已成功映射为网络磁盘，盘符：{MappedDrive}", device.Name, mappedDrive);
                                        }
                                        else
                                        {
                                            logger.LogWarning("网络磁盘映射失败，但未抛出异常，设备: {DeviceName}，IP: {IpAddress}，端口: {Port}", 
                                                device.Name, ipAddress, port);
                                        }
                                    }
                                    catch (Exception ex)
                                    {
                                        logger.LogError(ex, "网络磁盘映射失败，设备: {DeviceName}，IP: {IpAddress}，端口: {Port}", 
                                            device.Name, ipAddress, port);
                                    }
                                }
                            }
                        }
                    }
                    catch (JsonException ex)
                    {
                        logger.LogError(ex, "解析ftp消息失败: {decryptedPayload}", decryptedPayload);
                    }
                    break;
#endif
                    
                case "DATA_CLIPBOARD":
                    // 剪贴板消息，直接处理
                    logger.LogDebug("处理DATA_CLIPBOARD消息，内容: {decryptedPayload}", decryptedPayload);
                    try
                    {
                        // 直接解析为ClipboardMessage并处理
                        var doc = JsonDocument.Parse(decryptedPayload);
                        var root = doc.RootElement;
                        
                        // 提取剪贴板消息内容
                        var clipboardType = root.TryGetProperty("clipboardType", out var typeProp) ? typeProp.GetString() : "text/plain";
                        var content = root.TryGetProperty("content", out var contentProp) ? contentProp.GetString() : string.Empty;
                        
                        // 创建ClipboardMessage对象
                        var clipboardMessage = new ClipboardMessage
                        {
                            ClipboardType = clipboardType,
                            Content = content
                        };
                        
                        // 调用消息处理器处理剪贴板消息
                        await messageHandler.Value.HandleMessageAsync(device, clipboardMessage);
                        logger.LogDebug("已处理剪贴板消息，类型: {clipboardType}", clipboardType);
                    }
                    catch (JsonException ex)
                    {
                        logger.LogError(ex, "解析剪贴板消息失败: {decryptedPayload}", decryptedPayload);
                    }
                    break;
                    
                default:
                    logger.LogWarning("不支持的 DATA 消息类型: {messageType}", messageType);
                    break;
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "处理DATA消息时出错");
        }
    }

    /// <summary>
    /// 处理应用列表请求
    /// </summary>
    private async Task HandleAppListRequestAsync(PairedDevice device, string decryptedPayload)
    {
        try
        {
            // 应用列表请求暂时不处理，直接返回
            logger.LogDebug("收到应用列表请求，暂时不处理");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "处理应用列表请求时出错");
        }
    }

    /// <summary>
    /// 处理图标请求
    /// </summary>
    private async Task HandleIconRequestAsync(PairedDevice device, string decryptedPayload)
    {
        try
        {
            // 图标请求暂时不处理，直接返回
            logger.LogDebug("收到图标请求，暂时不处理");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "处理图标请求时出错");
        }
    }

    /// <summary>
    /// 处理媒体播放信息
    /// </summary>
    private async Task HandleMediaPlayAsync(PairedDevice device, string decryptedPayload)
    {
        try
        {
            logger.LogTrace("收到DATA_MEDIAPLAY消息，设备：{deviceId}", device.Id);
            
            // 直接使用JsonDocument解析DATA_MEDIAPLAY消息
            using JsonDocument doc = JsonDocument.Parse(decryptedPayload);
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
                MediaType = root.TryGetProperty("mediaType", out JsonElement mediaTypeElement) && mediaTypeElement.ValueKind == JsonValueKind.String ? mediaTypeElement.GetString() : null
            };
            
            var notificationService = Ioc.Default.GetRequiredService<INotificationService>();
            await notificationService.HandleMediaPlayNotification(device, notificationMessage);
        }
        catch (JsonException jsonEx)
        {
            logger.LogError(jsonEx, "解析DATA_MEDIAPLAY消息JSON时出错，消息内容：{payload}", decryptedPayload.Length > 100 ? decryptedPayload[..100] + "..." : decryptedPayload);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "处理DATA_MEDIAPLAY消息时出错");
        }
    }

    /// <summary>
    /// 处理图标响应消息
    /// </summary>
    private async Task HandleIconResponseAsync(PairedDevice device, string payload)
    {
        try
        {
            if (!payload.TrimStart().StartsWith('{') && !payload.TrimStart().StartsWith('['))
            {
                logger.LogWarning("跳过非 JSON 图标响应：{payload}", payload.Length > 50 ? payload[..50] + "..." : payload);
                return;
            }
            
            // 首先尝试解析JSON
            JsonDocument doc;
            JsonElement root;
            try
            {
                doc = JsonDocument.Parse(payload);
                root = doc.RootElement;
            }
            catch (JsonException ex)
            {
                logger.LogWarning("解析图标响应JSON时出错：{ex.Message}", ex.Message);
                return;
            }
            
            logger.LogDebug("处理ICON_RESPONSE消息");
            // 直接调用IconUtils保存图标
            var packageName = root.TryGetProperty("packageName", out var packageNameProp) ? packageNameProp.GetString() : null;
            var iconData = root.TryGetProperty("iconData", out var iconDataProp) ? iconDataProp.GetString() : null;
            
            if (!string.IsNullOrEmpty(packageName) && !string.IsNullOrEmpty(iconData))
            {
                await IconUtils.SaveAppIconToPathAsync(iconData, packageName);
                logger.LogDebug("已保存应用图标：{packageName}", packageName);
                // 触发应用图标更新
                var notificationService = Ioc.Default.GetRequiredService<INotificationService>();
                // 由于没有公开方法，我们只能记录日志
                logger.LogDebug("图标已保存，通知UI可能需要刷新");
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "处理图标响应时出错");
        }
    }

    /// <summary>
    /// 处理应用列表响应消息
    /// </summary>
    private async Task HandleAppListResponseAsync(PairedDevice device, string payload)
    {
        try
        {
            if (!payload.TrimStart().StartsWith('{') && !payload.TrimStart().StartsWith('['))
            {
                logger.LogWarning("跳过非 JSON 应用列表响应：{payload}", payload.Length > 50 ? payload[..50] + "..." : payload);
                return;
            }
            
            // 首先尝试解析JSON
            JsonDocument doc;
            JsonElement root;
            try
            {
                doc = JsonDocument.Parse(payload);
                root = doc.RootElement;
            }
            catch (JsonException ex)
            {
                logger.LogWarning("解析应用列表响应JSON时出错：{ex.Message}", ex.Message);
                return;
            }
            
            logger.LogDebug("处理APP_LIST_RESPONSE消息");
            // 直接处理应用列表响应
            var remoteAppRepository = Ioc.Default.GetRequiredService<RemoteAppRepository>();
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
                    // 直接构建图标请求并使用ProtocolSender发送，避免依赖INetworkService
                    var localDevice = await deviceManager.GetLocalDeviceAsync();
                    var localDeviceId = localDevice.DeviceId;
                    var localPublicKey = Encoding.UTF8.GetString(localDevice.PublicKey ?? Array.Empty<byte>());
                    
                    foreach (var packageName in packageNamesWithoutIcons)
                    {
                        // 构建图标请求对象
                        var requestObj = new
                        {
                            type = "ICON_REQUEST",
                            packageName = packageName,
                            time = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
                        };
                        
                        // 序列化为JSON
                        string requestJson = JsonSerializer.Serialize(requestObj);
                        
                        // 使用ProtocolSender发送请求
                        await ProtocolSender.SendEncryptedAsync(
                            logger,
                            device,
                            "DATA_ICON_REQUEST",
                            requestJson,
                            localDeviceId,
                            localPublicKey
                        );
                    }
                }
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "处理应用列表响应时出错");
        }
    }

    /// <summary>
    /// 处理普通通知消息
    /// </summary>
    private async Task HandleNotificationPayloadAsync(PairedDevice device, string payload)
    {
        try
        {
            if (!payload.TrimStart().StartsWith('{') && !payload.TrimStart().StartsWith('['))
            {
                logger.LogWarning("跳过非 JSON 通知载荷：{payload}", payload.Length > 50 ? payload[..50] + "..." : payload);
                return;
            }
            
            // 首先尝试解析JSON
            JsonDocument doc;
            JsonElement root;
            try
            {
                doc = JsonDocument.Parse(payload);
                root = doc.RootElement;
            }
            catch (JsonException ex)
            {
                logger.LogWarning("解析通知JSON时出错：{ex.Message}", ex.Message);
                return;
            }
            
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
            await messageHandler.Value.HandleMessageAsync(device, notificationMessage);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "处理普通通知消息时出错");
        }
    }

    /// <summary>
    /// 处理媒体控制消息
    /// </summary>
    private async Task HandleMediaControlMessageAsync(PairedDevice device, string payload)
    {
        try
        {
            logger.LogDebug("处理媒体控制消息：{payload}", payload.Length > 100 ? payload[..100] + "..." : payload);
            
            using var doc = JsonDocument.Parse(payload);
            var root = doc.RootElement;
            
            // 获取action字段
            if (root.TryGetProperty("action", out var actionProp))
            {
                var action = actionProp.GetString();
                logger.LogDebug("媒体控制动作：{action}", action);
                
                // 处理不同的媒体控制动作
                switch (action)
                {
                    case "playPause":
                    case "next":
                    case "previous":
                        // 执行媒体控制动作
                        logger.LogDebug("执行媒体控制动作：{action}", action);
                        try
                        {
                            var playbackService = Ioc.Default.GetRequiredService<IPlaybackService>();
                            PlaybackActionType actionType = action switch
                            {
                                "playPause" => PlaybackActionType.Play,
                                "next" => PlaybackActionType.Next,
                                "previous" => PlaybackActionType.Previous,
                                _ => PlaybackActionType.Play
                            };
                            await playbackService.HandleMediaActionAsync(new PlaybackAction
                            {
                                PlaybackActionType = actionType,
                                Source = "MediaControl"
                            });
                        }
                        catch (Exception ex)
                        {
                            logger.LogError(ex, "执行媒体控制动作时出错：{action}", action);
                        }
                        break;
                        
                    case "audioRequest":
                        // 处理音频转发请求
                        logger.LogDebug("收到音频转发请求");
                        try
                        {
                            // 构建仅音频转发的 scrcpy 参数
                            string customArgs = "--no-video --no-control";
                            
                            // 启动 scrcpy 仅音频转发
                            bool success = await screenMirrorService.StartScrcpy(device, customArgs);
                            
                            // 构造响应
                            var response = new
                            {
                                type = "MEDIA_CONTROL",
                                action = "audioResponse",
                                result = success ? "accepted" : "rejected"
                            };
                            string responseJson = JsonSerializer.Serialize(response);
                            
                            // 获取本地设备信息
                            var localDevice = await deviceManager.GetLocalDeviceAsync();
                            var localDeviceId = localDevice.DeviceId;
                            var localPublicKey = Encoding.UTF8.GetString(localDevice.PublicKey ?? Array.Empty<byte>());
                            
                            // 直接使用 ProtocolSender 发送响应，使用 DATA_MEDIA_CONTROL 协议头
                            await ProtocolSender.SendEncryptedAsync(
                                logger,
                                device,
                                "DATA_MEDIA_CONTROL",
                                responseJson,
                                localDeviceId,
                                localPublicKey
                            );
                            
                            logger.LogDebug("音频转发请求处理完成，结果：{result}", success ? "accepted" : "rejected");
                        }
                        catch (Exception ex)
                        {
                            logger.LogError(ex, "处理音频转发请求时出错");
                            
                            // 发送拒绝响应，使用 DATA_MEDIA_CONTROL 协议头
                            var errorResponse = new
                            {
                                type = "MEDIA_CONTROL",
                                action = "audioResponse",
                                result = "rejected"
                            };
                            string errorResponseJson = JsonSerializer.Serialize(errorResponse);
                            
                            // 获取本地设备信息
                            var localDevice = await deviceManager.GetLocalDeviceAsync();
                            var localDeviceId = localDevice.DeviceId;
                            var localPublicKey = Encoding.UTF8.GetString(localDevice.PublicKey ?? Array.Empty<byte>());
                            
                            // 直接使用 ProtocolSender 发送响应
                            await ProtocolSender.SendEncryptedAsync(
                                logger,
                                device,
                                "DATA_MEDIA_CONTROL",
                                errorResponseJson,
                                localDeviceId,
                                localPublicKey
                            );
                        }
                        break;
                }
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "处理媒体控制消息时出错");
        }
    }
}
