using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using CommunityToolkit.WinUI;
using Microsoft.UI.Dispatching;
using NotifyRelay.Data.Contracts;
using NotifyRelay.Native;
using NotifyRelay.Services.Devices;
using NotifyRelay.Services.Media;
using NotifyRelay.Services.Protocol;
using NotifyRelay.Services.Overlay;
using Windows.Media.Control;
using Windows.System;

namespace NotifyRelay.Platforms.Windows.Services;

/// <summary>
/// 播放数据构建与分发：从 SMTC 会话读取媒体属性，推送给 Rust 合并引擎、Overlay 与 Gamebar。
/// </summary>
public class PlaybackDataSyncer(
    ILogger<PlaybackDataSyncer> logger,
    IGeneralSettingsService generalSettings,
    IDeviceManager deviceManager,
    SmtcSessionRegistry sessionRegistry)
{
    private readonly Microsoft.UI.Dispatching.DispatcherQueue dispatcher = Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread();

    private static readonly ConcurrentDictionary<string, string> _appNameCache = new();

    /// <summary>会话媒体快照。内部传递用，替代原先的 JSON 字符串中转。</summary>
    private sealed record PlaybackSnapshot(string? Source, string? TrackTitle, string? Artist, string? Thumbnail, bool IsPlaying);

    /// <summary>启动 9 秒周期的媒体状态重推循环（fire-and-forget，与现状一致）。</summary>
    public void StartPeriodicSyncLoop()
    {
        // 定期发送媒体消息（每9秒发送一次）
        _ = Task.Run(async () =>
        {
            while (true)
            {
                await Task.Delay(TimeSpan.FromSeconds(9));
                try
                {
                    // 获取当前活跃的媒体会话
                    var currentSession = sessionRegistry.CurrentSession;
                    if (currentSession != null && sessionRegistry.Contains(currentSession.SourceAppUserModelId))
                    {
                        // 定期发送媒体数据，与Android端保持一致
                        await UpdatePlaybackDataAsync(currentSession);
                    }
                }
                catch (COMException comEx)
                {
                    // 忽略WinRT COM异常，避免频繁触发日志
                    logger.LogDebug(comEx, "定期更新播放数据时WinRT COM异常");
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "定期更新播放数据时出错");
                }
            }
        });
    }

    /// <summary>会话移除后的收尾：移除 Overlay 媒体卡片并向已连接设备推送结束标记。</summary>
    public void HandleSessionRemoved(string appUserModelId)
    {
        // 会话真正结束时移除 Overlay 媒体卡片
        if (generalSettings.DanmakuMediaCardEnabled)
        {
            try
            {
                var overlay = Ioc.Default.GetRequiredService<OverlayRenderService>();
                overlay.RemoveMediaCard(appUserModelId ?? "local");
                logger.LogDebug("HandleSessionRemoved: 移除媒体卡片 source={Source}", appUserModelId);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "移除 Overlay 媒体卡片失败");
            }
        }

        if (!generalSettings.EnableSendMediaNotifications) return;

        // 发送媒体结束通知：推送结束标记，Rust 合并引擎会回传 terminateValue="__END__" 全量。
        foreach (var device in deviceManager.PairedDevices)
        {
            if (device.ConnectionStatus && device.DeviceSettings.MediaSessionSyncEnabled)
            {
                NativeCore.PushMediaState(device.Id, "{}", true);
            }
        }
    }

    /// <summary>刷新并分发指定会话的播放数据。</summary>
    public async Task UpdatePlaybackDataAsync(GlobalSystemMediaTransportControlsSession session)
    {
        try
        {
            await dispatcher.EnqueueAsync(async () =>
            {
                var snapshot = await GetPlaybackSessionAsync(session);
                if (snapshot is null || !sessionRegistry.Contains(session.SourceAppUserModelId)) return;

                SendPlaybackData(snapshot);
            });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "更新播放数据时出错：{SourceAppUserModelId}", session.SourceAppUserModelId);
        }
    }

    private async Task<PlaybackSnapshot?> GetPlaybackSessionAsync(GlobalSystemMediaTransportControlsSession session)
    {
        try
        {
            // 只获取Android端需要的媒体字段
            var mediaProperties = await session.TryGetMediaPropertiesAsync();

            var source = session.SourceAppUserModelId;
            var trackTitle = mediaProperties.Title;
            var artist = mediaProperties.Artist ?? "Unknown Artist";
            string? thumbnail = null;

            // 只获取封面图片，其他字段不需要
            if (mediaProperties.Thumbnail is not null)
                thumbnail = await mediaProperties.Thumbnail.ToBase64Async();

            bool isPlaying = false;
            try
            {
                var pbInfo = session.GetPlaybackInfo();
                isPlaying = pbInfo?.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing;
            }
            catch { }

            return new PlaybackSnapshot(source, trackTitle, artist, thumbnail, isPlaying);
        }
        catch (COMException comEx)
        {
            // 忽略WinRT COM异常，避免频繁触发日志
            logger.LogDebug(comEx, "WinRT COM异常（获取播放数据）：{SourceAppUserModelId}", session.SourceAppUserModelId);
            return null;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "获取播放数据时出错：{SourceAppUserModelId}", session.SourceAppUserModelId);
            return null;
        }
    }

    private void SendPlaybackData(PlaybackSnapshot snapshot)
    {
        try
        {
            var source = snapshot.Source;
            var trackTitle = snapshot.TrackTitle;
            var artist = snapshot.Artist;
            var thumbnail = snapshot.Thumbnail;
            var isPlaying = snapshot.IsPlaying;

            logger.LogDebug("SendPlaybackData: source={Source}, trackTitle={Title}, artist={Artist}, isPlaying={IsPlaying}",
                source, trackTitle, artist, isPlaying);

            // Overlay 显示独立于远程发送开关
            var overlayEnabled = generalSettings.DanmakuMediaCardEnabled;
            var forceGamebar = generalSettings.GamebarRelayEnabled;

            // 远程推送（受 EnableSendMediaNotifications 控制）
            // 推送「全量」媒体状态；差异计算（FULL/DELTA）与合并由 Rust 合并引擎负责。
            bool shouldSendRemote = generalSettings.EnableSendMediaNotifications;
            if (shouldSendRemote)
            {
                // source 即 SourceAppUserModelId，用作 packageName；appName 取显示名，缺失时 fallback
                var appName = ResolveMediaAppName(source);
                string mediaJson = JsonSerializer.Serialize(new
                {
                    packageName = source ?? string.Empty,
                    appName = appName,
                    title = trackTitle ?? string.Empty,
                    text = artist ?? string.Empty,
                    coverUrl = thumbnail ?? string.Empty,
                    isPlaying = isPlaying
                });

                foreach (var device in deviceManager.PairedDevices)
                {
                    if (device.ConnectionStatus && device.DeviceSettings.MediaSessionSyncEnabled)
                    {
                        NativeCore.PushMediaState(device.Id, mediaJson, false);
                    }
                }
            }

            if (overlayEnabled)
            {
                try
                {
                    // SMTC 中 null/空值表示"未改变"而非"无数据"
                    // 此时跳过更新，保留上次有效的卡片，避免播放时闪烁
                    // 会话真正结束时会在 HandleSessionRemoved 中移除卡片
                    if (string.IsNullOrEmpty(trackTitle) && string.IsNullOrEmpty(artist))
                    {
                        logger.LogDebug("SendPlaybackData: 跳过空媒体数据，保留当前卡片 source={Source}", source);
                    }
                    else
                    {
                        byte[]? coverBytes = null;
                        if (!string.IsNullOrEmpty(thumbnail))
                        {
                            coverBytes = ImageHelper.FromBase64(thumbnail);
                        }
                        var overlay = Ioc.Default.GetRequiredService<OverlayRenderService>();
                        logger.LogDebug("SendPlaybackData: 显示媒体卡片 source={Source}, title={Title}", source, trackTitle);
                        overlay.ShowMediaCard(source ?? "local", "本机", trackTitle ?? "", artist ?? "", coverBytes, isPlaying);
                    }
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "发送媒体信息到 Overlay 失败");
                }
            }

            if (forceGamebar || !overlayEnabled)
            {
                // 空值表示"未改变"，跳过发送，避免 Gamebar 端闪烁
                if (string.IsNullOrEmpty(trackTitle) && string.IsNullOrEmpty(artist))
                {
                    logger.LogDebug("SendPlaybackData: 跳过 Gamebar 空媒体数据 source={Source}", source);
                }
                else
                {
                    try
                    {
                        _ = LocalSocketRelayServer.SendMediaInfoAsync(
                            source ?? "local",
                            "本机",
                            trackTitle ?? "",
                            artist ?? "",
                            thumbnail ?? "",
                            isPlaying
                        );
                    }
                    catch (Exception gamebarEx)
                    {
                        logger.LogError(gamebarEx, "发送媒体信息到 Gamebar 失败");
                    }
                }
            }

        }
        catch (Exception ex)
        {
            logger.LogError(ex, "发送播放数据时出错");
        }
    }

    /// <summary>
    /// 根据 SourceAppUserModelId 解析媒体应用显示名，缺失时 fallback 到本应用名。
    /// </summary>
    private string ResolveMediaAppName(string? sourceAppUserModelId)
    {
        if (string.IsNullOrWhiteSpace(sourceAppUserModelId))
            return "NotifyRelay";

        // 打包应用 AUMID 格式包含 '!'，尝试通过 AppInfo API 解析显示名
        if (sourceAppUserModelId.Contains('!'))
        {
            if (_appNameCache.TryGetValue(sourceAppUserModelId, out var cached))
                return cached;

            try
            {
                var appInfo = AppInfo.GetFromAppUserModelId(sourceAppUserModelId);
                var displayName = appInfo?.DisplayInfo?.DisplayName;
                if (!string.IsNullOrWhiteSpace(displayName))
                {
                    _appNameCache[sourceAppUserModelId] = displayName;
                    return displayName;
                }
            }
            catch
            {
                // packageQuery 权限不足或 AUMID 无效，fallback 到分割逻辑
            }

            // API 失败时 fallback：取 ! 之前的部分
            var fallbackName = sourceAppUserModelId.Split('!')[0];
            if (!string.IsNullOrWhiteSpace(fallbackName))
            {
                _appNameCache[sourceAppUserModelId] = fallbackName;
                return fallbackName;
            }
        }

        // 非打包输入（如 "Spotify.exe"）：直接使用
        var name = sourceAppUserModelId;

        // 去掉 .exe 后缀
        if (name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            name = name[..^4];

        return string.IsNullOrWhiteSpace(name) ? "NotifyRelay" : name;
    }
}
