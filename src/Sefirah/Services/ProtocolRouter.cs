using CommunityToolkit.Mvvm.DependencyInjection;
using NotifyRelay.Data.Contracts;
using NotifyRelay.Data.Enums;
using NotifyRelay.Data.Models;
using NotifyRelay.DeviceCtrl.AudioRelay;
using NotifyRelay.Models.Render;
using NotifyRelay.Native;
#if WINDOWS
using NotifyRelay.Platforms.Windows.Services;
#endif

using NotifyRelay.Services.Overlay;

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
    private readonly IGeneralSettingsService generalSettingsService;
    private readonly Lazy<INotificationService> notificationService;
    private readonly Lazy<IClipboardService> clipboardService;
    private readonly Lazy<IRemoteAppService> remoteAppService;
    private readonly Lazy<IPlaybackService> playbackService;
    private readonly AudioRelayService _audioRelayService;
#if WINDOWS
    private readonly Lazy<NetworkDriveMapper> networkDriveMapper;
#endif

    public ProtocolRouter(
        ILogger<ProtocolRouter> logger,
        IDeviceManager deviceManager,
        IScreenMirrorService screenMirrorService,
        IGeneralSettingsService generalSettingsService,
        Func<INotificationService> notificationServiceFactory,
        Func<IClipboardService> clipboardServiceFactory,
        Func<IRemoteAppService> remoteAppServiceFactory,
        Func<IPlaybackService> playbackServiceFactory,
        AudioRelayService audioRelayService
#if WINDOWS
        , Func<NetworkDriveMapper> networkDriveMapperFactory
#endif
        )
    {
        this.logger = logger;
        this.deviceManager = deviceManager;
        this.screenMirrorService = screenMirrorService;
        this.generalSettingsService = generalSettingsService;
        this.notificationService = new Lazy<INotificationService>(notificationServiceFactory);
        this.clipboardService = new Lazy<IClipboardService>(clipboardServiceFactory);
        this.remoteAppService = new Lazy<IRemoteAppService>(remoteAppServiceFactory);
        this.playbackService = new Lazy<IPlaybackService>(playbackServiceFactory);
        this._audioRelayService = audioRelayService;
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

    // ========= 已由 Rust 回调驱动的 DATA_* 独立处理方法 =========

    public Task OnDataNotificationAsync(PairedDevice device, string plaintext)
        => notificationService.Value.ProcessNotificationMessageAsync(device, plaintext);

    public async Task OnDataMediaPlayAsync(PairedDevice device, string plaintext)
    {
        if (!ShouldProcessMediaMessage(device))
        {
            logger.LogDebug("已忽略DATA_MEDIAPLAY消息: deviceId={deviceId} mode={mode}", device.Id, generalSettingsService.MediaMessageReceiveMode);
            var rawJson = JsonSerializer.Serialize(new { type = "DATA_MEDIAPLAY", mediaType = "END", time = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() });
            var json = rawJson;
            if (json != null)
            {
                await notificationService.Value.HandleMediaPlayNotification(device, json);
            }
            return;
        }
        await notificationService.Value.ProcessMediaPlayMessageAsync(device, plaintext);
    }

    public Task OnDataAppListResponseAsync(PairedDevice device, string plaintext)
        => remoteAppService.Value.ProcessAppListResponseAsync(device, plaintext);

    public Task OnDataIconResponseAsync(PairedDevice device, string plaintext)
        => notificationService.Value.ProcessIconResponseAsync(device, plaintext);

    public Task OnDataAudioRequestAsync(PairedDevice device, string plaintext)
        => notificationService.Value.ProcessNotificationMessageAsync(device, plaintext);

    public async Task OnDataMediaControlAsync(PairedDevice device, string plaintext)
    {
        logger.LogDebug("处理DATA_MEDIA_CONTROL消息，内容: {plaintext}", plaintext);
        try
        {
            using var doc = JsonDocument.Parse(plaintext);
            if (doc.RootElement.TryGetProperty("action", out var actionProp))
            {
                var action = actionProp.GetString();
                if (action == "audioRequest")
                {
                    await screenMirrorService.ProcessAudioRequestAsync(device);
                }
                else if (action == "audioStart")
                {
                    var sr = doc.RootElement.TryGetProperty("sampleRate", out var srProp) ? srProp.GetInt32() : 48000;
                    var ch = doc.RootElement.TryGetProperty("channels", out var chProp) ? chProp.GetInt32() : 2;
                    var ip = device.RemoteIpAddress ?? device.IpAddresses?.FirstOrDefault() ?? "";
                    logger.LogInformation("收到 audioStart, 启动音频接收: 远端={DeviceId}, IP={Ip}, 采样率={Sr}, 声道={Ch}", device.Id, ip, sr, ch);
                    await _audioRelayService.StartReceiveAsync(device.Id, ip, sr, ch);
                }
                else if (action == "audioStop")
                {
                    logger.LogInformation("收到 audioStop, 停止音频中继");
                    await _audioRelayService.StopAsync();
                }
                else
                {
                    PlaybackActionType actionType = action switch
                    {
                        "playPause" => PlaybackActionType.Play,
                        "next" => PlaybackActionType.Next,
                        "previous" => PlaybackActionType.Previous,
                        _ => PlaybackActionType.Play
                    };
                    var actionJson = JsonSerializer.Serialize(new
                    {
                        playbackActionType = actionType.ToString(),
                        source = "MediaControl"
                    });
                    await playbackService.Value.HandleMediaActionAsync(actionJson);
                }
            }
        }
        catch (Exception ex) { logger.LogError(ex, "处理DATA_MEDIA_CONTROL分发时出错"); }
    }

#if WINDOWS
    public Task OnDataFtpAsync(PairedDevice device, string plaintext)
        => networkDriveMapper.Value.ProcessFtpMessageAsync(device, plaintext);
#endif

    public Task OnDataClipboardAsync(PairedDevice device, string plaintext)
        => clipboardService.Value.ProcessClipboardMessageAsync(device, plaintext);

    public Task OnDataStatusAsync(PairedDevice device, string plaintext)
        => HandleStatusMessageAsync(device, plaintext);

    public Task OnDataAppListRequestAsync(PairedDevice device, string plaintext)
    {
        logger.LogDebug("收到应用列表请求，暂时不处理");
        return Task.CompletedTask;
    }

    public Task OnDataIconRequestAsync(PairedDevice device, string plaintext)
    {
        logger.LogDebug("收到图标请求，暂时不处理");
        return Task.CompletedTask;
    }

    public Task OnDataSuperIslandAsync(PairedDevice device, string plaintext)
        => HandleSuperIslandAsync(device, plaintext);

    private bool ShouldProcessMediaMessage(PairedDevice device)
    {
        return generalSettingsService.MediaMessageReceiveMode switch
        {
            MediaMessageReceiveMode.On => true,
            MediaMessageReceiveMode.Off => false,
            MediaMessageReceiveMode.AudioOnly => screenMirrorService.IsAudioOnlyRunning(device.Id),
            _ => true
        };
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
                var result = root.TryGetProperty("result", out var resultProp) ? resultProp.GetString() ?? string.Empty : string.Empty;
                var errorMessage = root.TryGetProperty("errorMessage", out var errorMessageProp) ? errorMessageProp.GetString() ?? string.Empty : string.Empty;
                var action = root.TryGetProperty("action", out var actionProp) ? actionProp.GetString() ?? string.Empty : string.Empty;

                logger.LogDebug("DATA_STATUS消息详情: originalHeader={originalHeader}, result={result}, action={action}", originalHeader, result, action);

                // 处理不同类型的状态响应
                switch (originalHeader)
                {
                    case "DATA_MEDIA_CONTROL":
                        logger.LogDebug("媒体控制状态响应: action={action}, result={result}", action, result);
                        break;
                    case "DATA_FTP":
                        logger.LogDebug("FTP状态响应: action={action}, result={result}", action, result);
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

    private async Task HandleSuperIslandAsync(PairedDevice device, string decryptedPayload)
    {
        try
        {
            using var doc = JsonDocument.Parse(decryptedPayload);
            var root = doc.RootElement;

            var type = TryGetString(root, "type");
            if (string.Equals(type, "SI_ACK", StringComparison.OrdinalIgnoreCase))
            {
                logger.LogDebug("收到超级岛ACK，忽略转发: deviceId={DeviceId}", device.Id);
                return;
            }

            var packageName = TryGetString(root, "packageName");
            var title = TryGetString(root, "title");
            var text = TryGetString(root, "text");
            var paramV2Raw = TryGetString(root, "param_v2_raw");
            var featureKeyValue = TryGetString(root, "featureKeyValue");
            if (string.IsNullOrWhiteSpace(featureKeyValue))
            {
                featureKeyValue = SuperIslandProtocol.ComputeFeatureId(packageName, paramV2Raw, title, text);
            }

            var sourceId = BuildSuperIslandSourceId(device.Id, packageName, featureKeyValue);
            var terminateValue = TryGetString(root, "terminateValue");
            var isEnd = string.Equals(terminateValue, SuperIslandProtocol.TerminateValue, StringComparison.Ordinal);
            var state = BuildSuperIslandState(root, title, text, paramV2Raw);
            var hasChanges = root.TryGetProperty("changes", out _);
            var pics = ParsePics(root);

            logger.LogInformation(
                "收到超级岛包: deviceId={DeviceId}, packageName={PackageName}, sourceId={SourceId}, isEnd={IsEnd}, hasChanges={HasChanges}",
                device.Id,
                packageName,
                sourceId,
                isEnd,
                hasChanges);

            // Priority chain: Overlay → Gamebar TCP
            var overlayEnabled = generalSettingsService.DanmakuSuperIslandEnabled;
            var forceGamebar = generalSettingsService.GamebarRelayEnabled;

            if (overlayEnabled)
            {
                var overlay = Ioc.Default.GetRequiredService<OverlayRenderService>();
                if (isEnd)
                {
                    overlay.RemoveSuperIsland(sourceId);
                }
                else
                {
                    var siState = new Models.Render.SuperIslandState
                    {
                        Title = title,
                        Subtitle = text,
                        ParamV2Raw = paramV2Raw,
                        Pics = pics,
                        IconPng = pics?.TryGetValue("icon", out var iconStr) == true ? Convert.FromBase64String(iconStr) : null,
                    };

                    // 解析 param_v2_raw 提取 Extra、进度、计时器
                    if (!string.IsNullOrWhiteSpace(paramV2Raw))
                    {
                        Models.Render.SuperIslandParamV2Parser.ApplyToState(siState, paramV2Raw);
                    }

                    // 处理增量变更
                    if (hasChanges)
                    {
                        var changesRaw = root.GetRawText();
                        siState.MergeChanges(changesRaw);
                    }

                    overlay.ShowSuperIsland(sourceId, device.Name, siState);
                }
            }

            if (forceGamebar || !overlayEnabled)
            {
                bool sent = await LocalSocketRelayServer.SendSuperIslandAsync(
                    device.Id,
                    device.Name,
                    sourceId,
                    isEnd,
                    state,
                    root.GetRawText());

                if (!sent && !overlayEnabled)
                {
                    logger.LogInformation("超级岛未找到Gamebar客户端，已忽略: deviceId={DeviceId}, sourceId={SourceId}", device.Id, sourceId);
                }
            }
        }
        catch (JsonException ex)
        {
            logger.LogWarning(ex, "解析超级岛消息失败");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "处理超级岛消息时出错");
        }
    }

    private static string? TryGetString(JsonElement root, string propertyName)
    {
        if (root.TryGetProperty(propertyName, out var prop) && prop.ValueKind == JsonValueKind.String)
        {
            return prop.GetString();
        }
        return null;
    }

    private static string BuildSuperIslandSourceId(string deviceId, string? packageName, string? featureId)
    {
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(deviceId)) parts.Add(deviceId);
        if (!string.IsNullOrWhiteSpace(packageName)) parts.Add(packageName);
        if (!string.IsNullOrWhiteSpace(featureId)) parts.Add(featureId);
        return string.Join("|", parts);
    }

    private static Dictionary<string, object?>? BuildSuperIslandState(
        JsonElement root,
        string? title,
        string? text,
        string? paramV2Raw)
    {
        var state = new Dictionary<string, object?>();
        if (!string.IsNullOrWhiteSpace(title)) state["title"] = title;
        if (!string.IsNullOrWhiteSpace(text)) state["text"] = text;
        if (!string.IsNullOrWhiteSpace(paramV2Raw)) state["param_v2_raw"] = paramV2Raw;

        var pics = ParsePics(root);
        if (pics != null && pics.Count > 0) state["pics"] = pics;

        return state.Count > 0 ? state : null;
    }

    private static Dictionary<string, string>? ParsePics(JsonElement root)
    {
        if (!root.TryGetProperty("pics", out var picsProp) || picsProp.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        var pics = new Dictionary<string, string>();
        foreach (var item in picsProp.EnumerateObject())
        {
            if (item.Value.ValueKind != JsonValueKind.String)
            {
                continue;
            }
            var value = item.Value.GetString();
            if (!string.IsNullOrWhiteSpace(value))
            {
                pics[item.Name] = value;
            }
        }

        return pics.Count > 0 ? pics : null;
    }

}

