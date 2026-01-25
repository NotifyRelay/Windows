using System.Text.Json;
using System.Text.Json.Nodes;
using NotifyRelay.Data.AppDatabase.Repository;
using NotifyRelay.Data.Contracts;
using NotifyRelay.Data.Enums;
using NotifyRelay.Data.Models;
using NotifyRelay.Helpers;
using NotifyRelay.Utils;
using NotifyRelay.Utils.Serialization;

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

    public ProtocolRouter(
        Func<IMessageHandler> messageHandlerFactory,
        ILogger<ProtocolRouter> logger,
        IDeviceManager deviceManager,
        IScreenMirrorService screenMirrorService)
    {
        this.messageHandler = new Lazy<IMessageHandler>(messageHandlerFactory);
        this.logger = logger;
        this.deviceManager = deviceManager;
        this.screenMirrorService = screenMirrorService;
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
                    // 普通通知和通用JSON数据
                    await DispatchPayloadAsync(device, decryptedPayload);
                    break;
                    
                case "DATA_MEDIAPLAY":
                    // 媒体播放信息，直接调用媒体播放通知处理
                    await HandleMediaPlayAsync(device, decryptedPayload);
                    break;
                    
                case "DATA_APP_LIST_RESPONSE":
                case "DATA_ICON_RESPONSE":
                case "DATA_AUDIO_REQUEST":
                    // 应用列表响应、图标响应和音频请求
                    await DispatchPayloadAsync(device, decryptedPayload);
                    break;
                    
                case "DATA_SUPERISLAND":
                    // 超级岛通知，直接忽略
                    break;
                    
                case "DATA_MEDIA_CONTROL":
                    // 媒体控制指令，直接处理
                    logger.LogDebug("处理DATA_MEDIA_CONTROL消息，内容: {decryptedPayload}", decryptedPayload);
                    try
                    {
                        // 直接解析媒体控制消息并处理
                        var doc = JsonDocument.Parse(decryptedPayload);
                        var root = doc.RootElement;
                        
                        // 提取媒体控制消息内容
                        var action = root.TryGetProperty("action", out var actionProp) ? actionProp.GetString() : string.Empty;
                        
                        // 创建PlaybackAction对象
                        var playbackAction = new PlaybackAction
                        {
                            PlaybackActionType = action switch
                            {
                                "playPause" => PlaybackActionType.Play,
                                "next" => PlaybackActionType.Next,
                                "previous" => PlaybackActionType.Previous,
                                _ => PlaybackActionType.Play
                            },
                            Source = "MediaControl"
                        };
                        
                        // 调用消息处理器处理媒体控制消息
                        await messageHandler.Value.HandleMessageAsync(device, playbackAction);
                        logger.LogDebug("已处理媒体控制消息，动作: {action}", action);
                    }
                    catch (JsonException ex)
                    {
                        logger.LogError(ex, "解析媒体控制消息失败: {decryptedPayload}", decryptedPayload);
                    }
                    break;
                
                case "DATA_SFTP":
                    // SFTP 消息，直接处理
                    logger.LogDebug("处理DATA_SFTP消息，内容: {decryptedPayload}", decryptedPayload);
                    try
                    {
                        var doc = JsonDocument.Parse(decryptedPayload);
                        var root = doc.RootElement;
                        
                        var action = root.TryGetProperty("action", out var actionProp) ? actionProp.GetString() : string.Empty;
                        logger.LogDebug("SFTP消息action: {action}", action);
                        
                        if (action == "started")
                        {
                            // SFTP服务已启动，解析服务器信息
                            if (root.TryGetProperty("ipAddress", out var ipAddressProp))
                            {
                                var ipAddress = ipAddressProp.GetString();
                                var port = root.TryGetProperty("port", out var portProp) ? portProp.GetInt32() : 22;
                                
                                if (!string.IsNullOrEmpty(ipAddress))
                                {
                                    // 安卓端不再发送用户名和密码，PC端需要从sharedSecret派生
                                    // 按照安卓端的逻辑从sharedSecret派生SFTP凭据
                                    // 1. 直接使用sharedSecret的字节数组（与安卓端完全一致）
                                    var sharedSecretBytes = device.SharedSecret;
                                    
                                    // 2. 使用SHA-256对sharedSecret字节数组进行哈希处理（与安卓端完全一致）
                                    using var sha256 = System.Security.Cryptography.SHA256.Create();
                                    var hashBytes = sha256.ComputeHash(sharedSecretBytes);
                                    
                                    // 3. 派生用户名：前缀"sftp_" + 哈希结果前8字节的Base64编码，取前16个字符，转换为小写
                                    var usernameBase64 = Convert.ToBase64String(hashBytes.AsSpan(0, 8));
                                    var cleanedUsername = System.Text.RegularExpressions.Regex.Replace(usernameBase64, "[^a-zA-Z0-9]", "");
                                    // 确保Substring参数正确，使用替换后的字符串长度
                                    var username = "sftp_" + cleanedUsername.Substring(0, Math.Min(16, cleanedUsername.Length)).ToLower();
                                    
                                    // 4. 派生密码：哈希结果前32字节的Base64编码，替换所有非字母数字字符
                                    var passwordBase64 = Convert.ToBase64String(hashBytes.AsSpan(0, 32));
                                    var password = System.Text.RegularExpressions.Regex.Replace(passwordBase64, "[^a-zA-Z0-9]", "");
                                    
                                    var sftpServerInfo = new SftpServerInfo
                                    {
                                        Username = username,
                                        Password = password,
                                        IpAddress = ipAddress,
                                        Port = port
                                    };
                                    
                                    var sftpService = Ioc.Default.GetRequiredService<ISftpService>();
                                    await sftpService.InitializeAsync(device, sftpServerInfo);
                                    logger.LogDebug("已初始化SFTP服务，IP: {IpAddress}, Port: {Port}", ipAddress, port);
                                }
                            }
                        }
                    }
                    catch (JsonException ex)
                    {
                        logger.LogError(ex, "解析SFTP消息失败: {decryptedPayload}", decryptedPayload);
                    }
                    break;
                    
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
    /// 分发载荷到对应的处理方法
    /// </summary>
    private async Task DispatchPayloadAsync(PairedDevice device, string payload)
    {
        try
        {
            if (!payload.TrimStart().StartsWith('{') && !payload.TrimStart().StartsWith('['))
            {
                logger.LogWarning("跳过非 JSON 载荷：{payload}", payload.Length > 50 ? payload[..50] + "..." : payload);
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
                logger.LogWarning("解析JSON时出错：{ex.Message}", ex.Message);
                return;
            }
            
            // 检查是否为ICON_RESPONSE消息
            if (root.TryGetProperty("packageName", out var packageNameProp) && 
                root.TryGetProperty("iconData", out var iconDataProp))
            {
                logger.LogDebug("处理ICON_RESPONSE消息");
                // 直接调用IconUtils保存图标
                var packageName = packageNameProp.GetString();
                var iconData = iconDataProp.GetString();
                
                if (!string.IsNullOrEmpty(packageName) && !string.IsNullOrEmpty(iconData))
                {
                    await IconUtils.SaveAppIconToPathAsync(iconData, packageName);
                    logger.LogDebug("已保存应用图标：{packageName}", packageName);
                    // 触发应用图标更新
                    var notificationService = Ioc.Default.GetRequiredService<INotificationService>();
                    // 由于没有公开方法，我们只能记录日志
                    logger.LogDebug("图标已保存，通知UI可能需要刷新");
                }
                return;
            }
            // 检查是否为APP_LIST_RESPONSE消息
            else if (root.TryGetProperty("apps", out var appsArray))
            {
                logger.LogDebug("处理APP_LIST_RESPONSE消息");
                // 直接处理应用列表响应
                var remoteAppRepository = Ioc.Default.GetRequiredService<RemoteAppRepository>();
                var appList = new ApplicationList { AppList = new List<ApplicationInfoMessage>() };
                
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
                    var networkService = Ioc.Default.GetRequiredService<INetworkService>();
                    foreach (var packageName in packageNamesWithoutIcons)
                    {
                        networkService.SendIconRequest(device.Id, packageName);
                    }
                }
                return;
            }
            // 检查是否为SFTP响应消息 - 已在ProcessDataMessageAsync中专门处理DATA_SFTP类型的消息，此处不再需要
            // 保留此注释，说明SFTP响应已在其他地方处理
            if (root.TryGetProperty("action", out var actionProp))
            {
                var action = actionProp.GetString() ?? string.Empty;
                if (action == "started" && root.TryGetProperty("ipAddress", out var _))
                {
                    logger.LogDebug("SFTP响应消息已在DATA_SFTP处理路径中处理，此处跳过");
                    return;
                }
            }
            
            // 尝试作为通知消息处理
            if (TryParseNotifyRelayNotification(payload, out var notificationMessage))
            {
                logger.LogDebug("处理为通知消息");
                await messageHandler.Value.HandleMessageAsync(device, notificationMessage);
                return;
            }

            // 尝试作为普通SocketMessage处理
            try
            {
                var socketMessage = SocketMessageSerializer.Deserialize<SocketMessage>(payload);
                if (socketMessage is not null)
                {
                    logger.LogDebug("处理为普通SocketMessage");
                    await messageHandler.Value.HandleMessageAsync(device, socketMessage);
                    return;
                }
            }
            catch (JsonException ex)
            {
                logger.LogDebug("解析SocketMessage时出错：{ex.Message}", ex.Message);
            }
            
            logger.LogWarning("无法处理的JSON载荷：{payload}", payload.Length > 100 ? payload[..100] + "..." : payload);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "分发载荷时出错");
        }
    }

    /// <summary>
    /// 尝试解析为NotifyRelay通知
    /// </summary>
    private bool TryParseNotifyRelayNotification(string payload, out NotificationMessage notificationMessage)
    {
        notificationMessage = null;
        try
        {
            using var doc = JsonDocument.Parse(payload);
            var root = doc.RootElement;

            // 检查是否包含必要的通知字段
            bool hasNotificationType = root.TryGetProperty("notificationType", out var notificationTypeProp);
            bool hasTitle = root.TryGetProperty("title", out var notificationTitleProp);
            bool hasText = root.TryGetProperty("text", out var notificationTextProp);
            
            if (hasNotificationType || hasTitle || hasText)
            {
                notificationMessage = new NotificationMessage
                {
                    NotificationKey = root.TryGetProperty("notificationKey", out var keyProp) && keyProp.ValueKind == JsonValueKind.String ? 
                        keyProp.GetString() : Guid.NewGuid().ToString(),
                    TimeStamp = root.TryGetProperty("timeStamp", out var timeProp) && timeProp.ValueKind == JsonValueKind.String ? 
                        timeProp.GetString() : DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString(),
                    NotificationType = hasNotificationType && notificationTypeProp.ValueKind == JsonValueKind.String ? 
                        Enum.TryParse<NotificationType>(notificationTypeProp.GetString(), true, out var type) ? type : NotificationType.New : NotificationType.New,
                    // 同时尝试获取packageName和appPackage字段
                    AppPackage = (root.TryGetProperty("packageName", out var packageNameProp) && packageNameProp.ValueKind == JsonValueKind.String ? packageNameProp.GetString() : null) ??
                                (root.TryGetProperty("appPackage", out var appPackageProp) && appPackageProp.ValueKind == JsonValueKind.String ? appPackageProp.GetString() : null),
                    AppName = root.TryGetProperty("appName", out var appNameProp) && appNameProp.ValueKind == JsonValueKind.String ? appNameProp.GetString() : null,
                    Title = hasTitle && notificationTitleProp.ValueKind == JsonValueKind.String ? notificationTitleProp.GetString() : null,
                    Text = hasText && notificationTextProp.ValueKind == JsonValueKind.String ? notificationTextProp.GetString() : null,
                    BigPicture = root.TryGetProperty("bigPicture", out var bigPictureProp) && bigPictureProp.ValueKind == JsonValueKind.String ? bigPictureProp.GetString() : null,
                    LargeIcon = root.TryGetProperty("largeIcon", out var largeIconProp) && largeIconProp.ValueKind == JsonValueKind.String ? largeIconProp.GetString() : null,
                    CoverUrl = root.TryGetProperty("coverUrl", out var coverUrlProp) && coverUrlProp.ValueKind == JsonValueKind.String ? coverUrlProp.GetString() : null,
                    MediaType = root.TryGetProperty("mediaType", out var mediaTypeProp) && mediaTypeProp.ValueKind == JsonValueKind.String ? mediaTypeProp.GetString() : null
                };
                return true;
            }
            return false;
        }
        catch (Exception ex)
        {
            logger.LogDebug("解析为通知消息时出错：{ex.Message}", ex.Message);
            return false;
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
                            // 发送响应，使用 DATA_MEDIA_CONTROL 协议头
                            
                            logger.LogDebug("音频转发请求处理完成，结果：{result}", success ? "accepted" : "rejected");
                        }
                        catch (Exception ex)
                        {
                            logger.LogError(ex, "处理音频转发请求时出错");
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
