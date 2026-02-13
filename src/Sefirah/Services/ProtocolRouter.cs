using NotifyRelay.Data.Contracts;
using NotifyRelay.Data.Enums;
using NotifyRelay.Data.Models;
using NotifyRelay.Helpers;
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
    private readonly ILogger<ProtocolRouter> logger;
    private readonly IDeviceManager deviceManager;
    private readonly IScreenMirrorService screenMirrorService;
    private readonly Lazy<INotificationService> notificationService;
    private readonly Lazy<IClipboardService> clipboardService;
    private readonly Lazy<IRemoteAppService> remoteAppService;
    private readonly Lazy<IPlaybackService> playbackService;
#if WINDOWS
    private readonly Lazy<NetworkDriveMapper> networkDriveMapper;
#endif

    public ProtocolRouter(
        ILogger<ProtocolRouter> logger,
        IDeviceManager deviceManager,
        IScreenMirrorService screenMirrorService,
        Func<INotificationService> notificationServiceFactory,
        Func<IClipboardService> clipboardServiceFactory,
        Func<IRemoteAppService> remoteAppServiceFactory,
        Func<IPlaybackService> playbackServiceFactory
#if WINDOWS
        , Func<NetworkDriveMapper> networkDriveMapperFactory
#endif
        )
    {
        this.logger = logger;
        this.deviceManager = deviceManager;
        this.screenMirrorService = screenMirrorService;
        this.notificationService = new Lazy<INotificationService>(notificationServiceFactory);
        this.clipboardService = new Lazy<IClipboardService>(clipboardServiceFactory);
        this.remoteAppService = new Lazy<IRemoteAppService>(remoteAppServiceFactory);
        this.playbackService = new Lazy<IPlaybackService>(playbackServiceFactory);
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
                    await notificationService.Value.ProcessNotificationMessageAsync(device, decryptedPayload);
                    break;

                case "DATA_MEDIAPLAY":
                    // 媒体播放信息
                    await notificationService.Value.ProcessMediaPlayMessageAsync(device, decryptedPayload);
                    break;

                case "DATA_APP_LIST_RESPONSE":
                    // 应用列表响应
                    await remoteAppService.Value.ProcessAppListResponseAsync(device, decryptedPayload);
                    break;

                case "DATA_ICON_RESPONSE":
                    // 图标响应
                    await notificationService.Value.ProcessIconResponseAsync(device, decryptedPayload);
                    break;

                case "DATA_AUDIO_REQUEST":
                    // 音频请求
                    await notificationService.Value.ProcessNotificationMessageAsync(device, decryptedPayload);
                    break;

                case "DATA_SUPERISLAND":
                    // 超级岛通知，直接忽略
                    break;

                case "DATA_MEDIA_CONTROL":
                    // 媒体控制指令，解析后分发
                    logger.LogDebug("处理DATA_MEDIA_CONTROL消息，内容: {decryptedPayload}", decryptedPayload);
                    try
                    {
                        using (JsonDocument doc = JsonDocument.Parse(decryptedPayload))
                        {
                            if (doc.RootElement.TryGetProperty("action", out var actionProp))
                            {
                                var action = actionProp.GetString();
                                if (action == "audioRequest")
                                {
                                    await screenMirrorService.ProcessAudioRequestAsync(device);
                                }
                                else
                                {
                                    // 其他动作 (playPause, next, previous) 交给 PlaybackService
                                    PlaybackActionType actionType = action switch
                                    {
                                        "playPause" => PlaybackActionType.Play,
                                        "next" => PlaybackActionType.Next,
                                        "previous" => PlaybackActionType.Previous,
                                        _ => PlaybackActionType.Play
                                    };
                                    await playbackService.Value.HandleMediaActionAsync(new PlaybackAction
                                    {
                                        PlaybackActionType = actionType,
                                        Source = "MediaControl"
                                    });
                                }
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        logger.LogError(ex, "处理DATA_MEDIA_CONTROL分发时出错");
                    }
                    break;
#if WINDOWS
                case "DATA_FTP":
                    await networkDriveMapper.Value.ProcessFtpMessageAsync(device, decryptedPayload);
                    break;
#endif
                case "DATA_CLIPBOARD":
                    await clipboardService.Value.ProcessClipboardMessageAsync(device, decryptedPayload);
                    break;

                case "DATA_STATUS":
                    // 处理状态响应消息
                    await HandleStatusMessageAsync(device, decryptedPayload);
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
    /// 处理状态响应消息
    /// </summary>
    private async Task HandleStatusMessageAsync(PairedDevice device, string decryptedPayload)
    {
        try
        {
            logger.LogDebug("处理DATA_STATUS消息: {decryptedPayload}", decryptedPayload.Length > 100 ? decryptedPayload[..100] + "..." : decryptedPayload);

            using (JsonDocument doc = JsonDocument.Parse(decryptedPayload))
            {
                var root = doc.RootElement;

                // 提取关键信息
                var originalHeader = root.TryGetProperty("originalHeader", out var originalHeaderProp) ? originalHeaderProp.GetString() : string.Empty;
                var result = root.TryGetProperty("result", out var resultProp) ? resultProp.GetString() : string.Empty;
                var errorMessage = root.TryGetProperty("errorMessage", out var errorMessageProp) ? errorMessageProp.GetString() : string.Empty;
                var action = root.TryGetProperty("action", out var actionProp) ? actionProp.GetString() : string.Empty;

                logger.LogDebug("DATA_STATUS消息详情: originalHeader={originalHeader}, result={result}, action={action}", originalHeader, result, action);

                // 处理不同类型的状态响应
                switch (originalHeader)
                {
                    case "DATA_MEDIA_CONTROL":
                        // 处理媒体控制响应
                        await HandleMediaControlResponseAsync(device, root, result, errorMessage, action);
                        break;
                    case "DATA_FTP":
                        // 处理FTP响应
                        HandleFtpResponse(device, root, result, errorMessage, action);
                        break;
                    default:
                        // 处理其他类型的状态响应
                        logger.LogDebug("处理其他类型的状态响应: {originalHeader}", originalHeader);
                        break;
                }
            }
        }
        catch (JsonException jsonEx)
        {
            logger.LogError(jsonEx, "解析DATA_STATUS消息时出错");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "处理DATA_STATUS消息时出错");
        }
    }

    /// <summary>
    /// 处理媒体控制响应
    /// </summary>
    private async Task HandleMediaControlResponseAsync(PairedDevice device, JsonElement root, string result, string errorMessage, string action)
    {
        try
        {
            logger.LogDebug("处理媒体控制响应: action={action}, result={result}", action, result);

            // 这里可以添加媒体控制响应的处理逻辑
            // 例如：更新媒体控制状态、显示提示信息等

            if (!string.IsNullOrEmpty(errorMessage))
            {
                logger.LogWarning("媒体控制响应错误: {errorMessage}", errorMessage);
                // 可以添加错误处理逻辑
            }
            else if (result == "success")
            {
                logger.LogDebug("媒体控制操作成功: {action}", action);
                // 可以添加成功处理逻辑
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "处理媒体控制响应时出错");
        }
    }

    /// <summary>
    /// 处理FTP响应
    /// </summary>
    private void HandleFtpResponse(PairedDevice device, JsonElement root, string result, string errorMessage, string action)
    {
        try
        {
            logger.LogDebug("处理FTP响应: action={action}, result={result}", action, result);

            // 这里可以添加FTP响应的处理逻辑
            // 例如：更新FTP状态、显示提示信息等

            if (!string.IsNullOrEmpty(errorMessage))
            {
                logger.LogWarning("FTP响应错误: {errorMessage}", errorMessage);
                // 可以添加错误处理逻辑
            }
            else if (result == "success")
            {
                logger.LogDebug("FTP操作成功: {action}", action);
                // 可以添加成功处理逻辑
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "处理FTP响应时出错");
        }
    }
}
