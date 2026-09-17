using CommunityToolkit.WinUI;
using NotifyRelay.Data.Contracts;
using NotifyRelay.Data.Models;
using NotifyRelay.Helpers;
// LocalSocketRelayServer 现位于 NotifyRelay.Services.Protocol
using NotifyRelay.Services.Protocol;
using NotifyRelay.Services.Overlay;

namespace NotifyRelay.Services.Notifications;

/// <summary>
/// 音乐媒体块管理服务：媒体块生命周期、超时检查与封面转换。
/// </summary>
public class MusicMediaBlockManager(
    ILogger logger,
    Microsoft.UI.Dispatching.DispatcherQueue dispatcher,
    IGeneralSettingsService generalSettings,
    OverlayRenderService overlayRender) : IMusicMediaBlockManager
{
    // 音乐媒体块相关（支持多个设备同时显示）
    private readonly ObservableCollection<MusicMediaBlock> _currentMusicMediaBlocks = new();
    private ReadOnlyObservableCollection<MusicMediaBlock>? _currentMusicMediaBlocksReadOnly;
    private System.Threading.Timer? _musicMediaBlockTimer;
    private const int MUSIC_MEDIA_BLOCK_TIMEOUT = 30; // 30秒超时

    /// <summary>
    /// 当前显示的音乐媒体块列表（只读，支持多个设备同时显示）
    /// </summary>
    public ReadOnlyObservableCollection<MusicMediaBlock> Blocks => _currentMusicMediaBlocksReadOnly ??= new ReadOnlyObservableCollection<MusicMediaBlock>(_currentMusicMediaBlocks);

    /// <summary>
    /// 启动 1 秒周期的超时检查定时器
    /// </summary>
    public void StartTimeoutChecker()
    {
        // 初始化音乐媒体块超时检查定时器，每1秒检查一次
        _musicMediaBlockTimer = new System.Threading.Timer(
            _ => CheckMusicMediaBlockTimeout(),
            null,
            TimeSpan.FromSeconds(1),
            TimeSpan.FromSeconds(1));
    }

    /// <summary>
    /// 处理媒体播放通知
    /// </summary>
    /// <param name="device">设备</param>
    /// <param name="notificationMessage">通知消息</param>
    public async Task HandleMediaPlayNotification(PairedDevice device, string payload)
    {
        try
        {
            if (!device.DeviceSettings.NotificationSyncEnabled)
            {
                return;
            }

            using var doc = JsonDocument.Parse(payload);
            var root = doc.RootElement;

            var mediaType = root.TryGetProperty("mediaType", out var mtProp) ? mtProp.GetString() : null;
            var titleStr = root.TryGetProperty("title", out var tProp) ? tProp.GetString() ?? "" : "";
            var textStr = root.TryGetProperty("text", out var txProp) ? txProp.GetString() ?? "" : "";
            var coverUrl = root.TryGetProperty("coverUrl", out var cuProp) ? cuProp.GetString() : null
                ?? (root.TryGetProperty("bigPicture", out var bpProp) ? bpProp.GetString() : null)
                ?? (root.TryGetProperty("largeIcon", out var liProp) ? liProp.GetString() : null);

            // 解析播放状态：缺省视为播放中（与 SendMediaInfoAsync 行为一致）
            bool isPlaying = true;
            if (root.TryGetProperty("isPlaying", out var ipProp))
            {
                if (ipProp.ValueKind == JsonValueKind.False) isPlaying = false;
                else if (ipProp.ValueKind == JsonValueKind.True) isPlaying = true;
            }

            // 叠加层媒体卡片开关（与本地媒体一致）
            var mediaOverlayEnabled = generalSettings.DanmakuMediaCardEnabled;

            if (mediaType == "END")
            {
                await dispatcher.EnqueueAsync(async () =>
                {
                    try
                    {
                        var existingBlock = _currentMusicMediaBlocks.FirstOrDefault(b => b.DeviceId == device.Id);
                        if (existingBlock != null)
                        {
                            _currentMusicMediaBlocks.Remove(existingBlock);
                            _ = LocalSocketRelayServer.SendMediaInfoAsync(device.Id, device.Name, "", "", "", false);
                        }

                        if (mediaOverlayEnabled)
                        {
                            overlayRender.RemoveMediaCard(device.Id);
                        }
                    }
                    catch (Exception ex)
                    {
                        logger.LogError(ex, "移除媒体块时出错，设备：{deviceId}", device.Id);
                    }
                });
                return;
            }

            await dispatcher.EnqueueAsync(async () =>
            {
                try
                {
                    var existingBlock = _currentMusicMediaBlocks.FirstOrDefault(b => b.DeviceId == device.Id);
                    if (existingBlock == null)
                    {
                        var newBlock = new MusicMediaBlock(
                            device.Id,
                            device.Name,
                            titleStr,
                            textStr,
                            coverUrl
                        );
                        _currentMusicMediaBlocks.Add(newBlock);
                        _ = LocalSocketRelayServer.SendMediaInfoAsync(device.Id, device.Name, titleStr, textStr, coverUrl ?? "", true);

                        if (mediaOverlayEnabled)
                        {
                            var coverBytes = ImageHelper.FromBase64(coverUrl);
                            overlayRender.ShowMediaCard(device.Id, device.Name, titleStr, textStr, coverBytes, isPlaying);
                        }
                    }
                    else
                    {
                        string updatedTitle = existingBlock.Title;
                        string updatedText = existingBlock.Text;
                        string? updatedCoverUrl = existingBlock.CoverUrl;

                        if (!string.IsNullOrEmpty(titleStr))
                        {
                            updatedTitle = titleStr;
                        }

                        if (!string.IsNullOrEmpty(textStr))
                        {
                            updatedText = textStr;
                        }

                        if (!string.IsNullOrEmpty(coverUrl))
                        {
                            updatedCoverUrl = coverUrl;
                        }

                        existingBlock.Update(updatedTitle, updatedText, updatedCoverUrl);
                        _ = LocalSocketRelayServer.SendMediaInfoAsync(device.Id, device.Name, updatedTitle, updatedText, updatedCoverUrl ?? "", true);

                        if (mediaOverlayEnabled)
                        {
                            var coverBytes = ImageHelper.FromBase64(updatedCoverUrl);
                            overlayRender.ShowMediaCard(device.Id, device.Name, updatedTitle, updatedText, coverBytes, isPlaying);
                        }
                    }
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "在UI线程上处理媒体播放通知时出错");
                }
            });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "处理媒体播放通知时出错");
        }
    }

    /// <summary>
    /// 检查音乐媒体块是否超时
    /// </summary>
    public void CheckMusicMediaBlockTimeout()
    {
        dispatcher.EnqueueAsync(async () =>
        {
            // 检查集合中每个媒体块是否超时，超时则移除
            var toRemove = _currentMusicMediaBlocks.Where(b => b.IsTimeout(MUSIC_MEDIA_BLOCK_TIMEOUT)).ToList();
            foreach (var b in toRemove)
            {
                try
                {
                    _currentMusicMediaBlocks.Remove(b);

                    if (generalSettings.DanmakuMediaCardEnabled)
                    {
                        overlayRender.RemoveMediaCard(b.DeviceId);
                    }
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "移除超时的音乐媒体块时出错，设备：{deviceId}", b.DeviceId);
                }

                _ = LocalSocketRelayServer.SendMediaInfoAsync(b.DeviceId, b.DeviceName, "", "", "", false);
            }
        });
    }

    public async Task ProcessMediaPlayMessageAsync(PairedDevice device, string payload)
    {
        try
        {
            logger.LogTrace("收到DATA_MEDIAPLAY消息，设备：{deviceId}", device.Id);

            // 检查是否为结束包
            using var doc = JsonDocument.Parse(payload);
            var root = doc.RootElement;
            var terminateValue = root.TryGetProperty("terminateValue", out var tv) && tv.ValueKind == JsonValueKind.String ? tv.GetString() : null;

            string finalPayload = payload;
            if (terminateValue != null && terminateValue.Equals("__END__", StringComparison.OrdinalIgnoreCase))
            {
                // 构造结束标记payload
                var rawJson = JsonSerializer.Serialize(new { type = "DATA_MEDIAPLAY", mediaType = "END", time = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() });
                var validatedJson = rawJson;
                if (validatedJson != null) finalPayload = validatedJson;
            }

            await HandleMediaPlayNotification(device, finalPayload);
        }
        catch (JsonException jsonEx)
        {
            logger.LogError(jsonEx, "解析DATA_MEDIAPLAY消息JSON时出错，消息内容：{payload}", payload.Length > 100 ? payload[..100] + "..." : payload);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "处理DATA_MEDIAPLAY消息时出错");
        }
    }
}
