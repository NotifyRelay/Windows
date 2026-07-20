using System.Runtime.InteropServices;
using CommunityToolkit.WinUI;
using Microsoft.UI.Dispatching;
using NAudio.CoreAudioApi;
using NAudio.CoreAudioApi.Interfaces;
using NAudio.Utils;
using NotifyRelay.Data.Contracts;
using NotifyRelay.Data.Enums;
using NotifyRelay.Data.Models;
using NotifyRelay.Helpers;
using NotifyRelay.Native;
using NotifyRelay.Platforms.Windows.Interop;
using Windows.Media;
using Windows.Media.Control;
using NotifyRelay.DeviceCtrl.VirtualSpeaker;
using NotifyRelay.Services;

namespace NotifyRelay.Platforms.Windows.Services;

public class WindowsPlaybackService(
    ILogger<WindowsPlaybackService> logger,
    ISessionManager sessionManager,
    IDeviceManager deviceManager,
    IProtocolSender protocolSender,
    IGeneralSettingsService generalSettings,
    VirtualSpeakerService virtualSpeaker) : IPlaybackService, IMMNotificationClient
{
    private readonly DispatcherQueue dispatcher = DispatcherQueue.GetForCurrentThread();
    private readonly Dictionary<string, GlobalSystemMediaTransportControlsSession> activeSessions = [];
    private GlobalSystemMediaTransportControlsSessionManager? manager;

    // Local SMTC for remote media display

    public List<AudioDevice> AudioDevices { get; private set; } = [];
    private readonly MMDeviceEnumerator enumerator = new();

    private readonly Dictionary<string, double> lastTimelinePosition = [];
    private readonly Dictionary<string, DateTime> lastSessionUpdateTime = [];
    private const int MinUpdateIntervalMs = 5000; // 最小更新间隔，5秒

    // 媒体播放状态跟踪，用于实现差异包发送
    private readonly Dictionary<string, MediaPlayState> lastMediaState = [];

    // 媒体播放状态数据类，用于跟踪上次发送的媒体状态
    private class MediaPlayState
    {
        public string? Title { get; set; }
        public string? Artist { get; set; }
        public string? Thumbnail { get; set; }
        public DateTime SentTime { get; set; }
    }

    // 内部播放数据类，替代已删除的 PlaybackSession
    private class PlaybackData
    {
        public SessionType SessionType { get; set; }
        public string? Source { get; set; }
        public string? TrackTitle { get; set; }
        public string? Artist { get; set; }
        public string? Thumbnail { get; set; }
        public bool IsPlaying { get; set; }
        public double? Position { get; set; }
    }

    /// <inheritdoc/>
    public async Task InitializeAsync()
    {
        try
        {
            manager = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
            if (manager is null)
            {
                logger.LogError("初始化系统媒体传输控制会话管理器失败");
                return;
            }

            GetAllAudioDevices();
            UpdateActiveSessions();

            enumerator.RegisterEndpointNotificationCallback(this);

            manager.SessionsChanged += SessionsChanged;

            sessionManager.ConnectionStatusChanged += async (sender, args) =>
            {
                //if (args.IsConnected && args.Device.DeviceSettings?.MediaSessionSyncEnabled == true)
                //{
                //foreach (var session in activeSessions.Values)
                //{
                //    await UpdatePlaybackDataAsync(session);
                //}
                //foreach (var device in AudioDevices)
                //{
                //    device.AudioDeviceType = AudioMessageType.New;
                //    string jsonMessage = SocketMessageSerializer.Serialize(device);
                //    sessionManager.SendMessage(args.Device.Id, jsonMessage);
                // }
                //}
            };

            // 定期发送媒体消息（每9秒发送一次）
            _ = Task.Run(async () =>
            {
                while (true)
                {
                    await Task.Delay(TimeSpan.FromSeconds(9));
                    try
                    {
                        // 获取当前活跃的媒体会话
                        var currentSession = manager.GetCurrentSession();
                        if (currentSession != null && activeSessions.ContainsKey(currentSession.SourceAppUserModelId))
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

            logger.LogInformation("播放服务初始化成功");
        }
        catch (COMException comEx)
        {
            // 忽略WinRT COM异常，避免频繁触发日志
            logger.LogDebug(comEx, "初始化播放服务时WinRT COM异常");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "初始化播放服务失败");
        }
    }

    public async Task HandleMediaActionAsync(string mediaActionJson)
    {
        var (source, actionType, value) = ParseMediaActionData(mediaActionJson);
        if (source == null) return;

        // 尝试根据Source字段查找对应的媒体会话
        var session = activeSessions.Values.FirstOrDefault(s => s.SourceAppUserModelId == source);

        // 如果找不到匹配的会话，或者Source是"MediaControl"（来自外部设备的控制指令），则使用当前活动的媒体会话
        if (session == null || source == "MediaControl")
        {
            session = manager?.GetCurrentSession();
        }

        // 检查是否是本应用自身的媒体会话，如果是则不执行控制指令
        if (session != null)
        {
            // 获取当前进程的名称，用于识别本应用的媒体会话
            string currentProcessName = System.Diagnostics.Process.GetCurrentProcess().ProcessName;

            // 检查媒体会话的SourceAppUserModelId是否包含当前进程名称
            // 如果包含则认为是本应用自身的媒体会话，不执行控制指令
            if (!string.IsNullOrEmpty(session.SourceAppUserModelId) &&
                session.SourceAppUserModelId.Contains(currentProcessName, StringComparison.OrdinalIgnoreCase))
            {
                logger.LogDebug("忽略对本应用自身媒体会话的控制指令");
                return;
            }
        }

        // 执行媒体操作并发送响应
        bool success = await ExecuteSessionActionAsync(session, source, actionType, value);

        // 发送媒体操作响应
        SendMediaControlResponse(source, actionType, success);
    }

    private static (string? Source, string? ActionType, double? Value) ParseMediaActionData(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            var source = root.TryGetProperty("source", out var s) ? s.GetString() : null;
            var actionType = root.TryGetProperty("playbackActionType", out var a) ? a.GetString() : null;
            double? value = root.TryGetProperty("value", out var v) && v.ValueKind == JsonValueKind.Number ? v.GetDouble() : null;
            return (source, actionType, value);
        }
        catch
        {
            return (null, null, null);
        }
    }

    private async Task<bool> ExecuteSessionActionAsync(GlobalSystemMediaTransportControlsSession? session, string? source, string? actionType, double? value)
    {
        bool success = false;

        await dispatcher.EnqueueAsync(async () =>
        {
            try
            {
                if (session == null)
                {
                    logger.LogWarning("没有活跃的媒体会话，无法执行操作：{actionType}", actionType);
                    success = false;
                    return;
                }

                switch (actionType)
                {
                    case "Play":
                    case "playPause":
                        if (source == "MediaControl")
                        {
                            var playbackInfo = session?.GetPlaybackInfo();
                            if (playbackInfo != null)
                            {
                                if (playbackInfo.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing)
                                {
                                    var result = await session?.TryPauseAsync();
                                    success = result == true;
                                }
                                else
                                {
                                    var result = await session?.TryPlayAsync();
                                    success = result == true;
                                }
                            }
                            else
                            {
                                var result = await session?.TryPlayAsync();
                                success = result == true;
                            }
                        }
                        else
                        {
                            var result = await session?.TryPlayAsync();
                            success = result == true;
                        }
                        break;
                    case "Pause":
                        var pauseResult = await session?.TryPauseAsync();
                        success = pauseResult == true;
                        break;
                    case "Next":
                        var nextResult = await session?.TrySkipNextAsync();
                        success = nextResult == true;
                        break;
                    case "Previous":
                        var prevResult = await session?.TrySkipPreviousAsync();
                        success = prevResult == true;
                        break;
                    case "Seek":
                        if (value.HasValue)
                        {
                            TimeSpan position = TimeSpan.FromMilliseconds(value.Value);
                            var seekResult = await session?.TryChangePlaybackPositionAsync(position.Ticks);
                            success = seekResult == true;
                        }
                        break;
                    case "Shuffle":
                        var shuffleResult = await session?.TryChangeShuffleActiveAsync(true);
                        success = shuffleResult == true;
                        break;
                    case "Repeat":
                        if (value.HasValue)
                        {
                            if (value.Value == 1.0)
                            {
                                var repeatResult = await session?.TryChangeAutoRepeatModeAsync(MediaPlaybackAutoRepeatMode.Track);
                                success = repeatResult == true;
                            }
                            else if (value.Value == 2.0)
                            {
                                var repeatResult = await session?.TryChangeAutoRepeatModeAsync(MediaPlaybackAutoRepeatMode.List);
                                success = repeatResult == true;
                            }
                        }
                        break;
                    case "DefaultDevice":
                        SetDefaultAudioDevice(source);
                        success = true;
                        break;
                    case "VolumeUpdate":
                        if (value.HasValue)
                        {
                            SetVolume(source, Convert.ToSingle(value.Value));
                            success = true;
                        }
                        break;
                    case "ToggleMute":
                        ToggleMute(source);
                        success = true;
                        break;
                    default:
                        logger.LogWarning("未处理的媒体操作：{actionType}", actionType);
                        success = false;
                        break;
                }
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "执行媒体操作时出错：{actionType}", actionType);
                success = false;
            }
        });

        return success;
    }

    private void SessionsChanged(GlobalSystemMediaTransportControlsSessionManager manager, SessionsChangedEventArgs args)
    {
        UpdateSessionsList(manager.GetSessions());
    }

    private void UpdateActiveSessions()
    {
        if (manager is null) return;

        try
        {
            var activeSessions = manager.GetSessions();
            UpdateSessionsList(activeSessions);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "更新活动会话时出错");
        }
    }

    private void UpdateSessionsList(IReadOnlyList<GlobalSystemMediaTransportControlsSession> activeSessions)
    {
        lock (this.activeSessions)
        {
            var currentSessionIds = new HashSet<string>(activeSessions.Select(s => s.SourceAppUserModelId));

            foreach (var sessionId in this.activeSessions.Keys.ToList())
            {
                if (!currentSessionIds.Contains(sessionId))
                {
                    RemoveSession(sessionId);
                }
            }

            foreach (var session in activeSessions.Where(s => s is not null))
            {
                if (!this.activeSessions.ContainsKey(session.SourceAppUserModelId))
                {
                    AddSession(session);
                }
            }
        }
    }

    private void RemoveSession(string sessionId)
    {
        if (activeSessions.TryGetValue(sessionId, out var session))
        {
            activeSessions.Remove(sessionId);
            UnsubscribeFromSessionEvents(session);
        }
    }

    private void AddSession(GlobalSystemMediaTransportControlsSession session)
    {
        if (!activeSessions.ContainsKey(session.SourceAppUserModelId))
        {
            activeSessions[session.SourceAppUserModelId] = session;
            lastTimelinePosition[session.SourceAppUserModelId] = 0;
            SubscribeToSessionEvents(session);
        }
    }

    private void SubscribeToSessionEvents(GlobalSystemMediaTransportControlsSession session)
    {
        session.TimelinePropertiesChanged += Session_TimelinePropertiesChanged;
        session.MediaPropertiesChanged += Session_MediaPropertiesChanged;
        session.PlaybackInfoChanged += Session_PlaybackInfoChanged;
    }

    private void Session_TimelinePropertiesChanged(GlobalSystemMediaTransportControlsSession sender, TimelinePropertiesChangedEventArgs args)
    {
        try
        {
            if (!activeSessions.ContainsKey(sender.SourceAppUserModelId)) return;
            var timelineProperties = sender.GetTimelineProperties();
            var isCurrentSession = manager?.GetCurrentSession()?.SourceAppUserModelId == sender.SourceAppUserModelId;

            if (timelineProperties is null || !isCurrentSession) return;

            if (lastTimelinePosition.TryGetValue(sender.SourceAppUserModelId, out var lastPosition))
            {
                double currentPosition = timelineProperties.Position.TotalMilliseconds;
                if (Math.Abs(currentPosition - lastPosition) < 1000) return; // Ignore minor changes under 1 second

                lastTimelinePosition[sender.SourceAppUserModelId] = currentPosition;

                // 时间线变化时只发送位置信息，不发送完整媒体信息以减少网络流量
                // 如果需要完整信息，会通过MediaPropertiesChanged事件发送
                var rawJson = JsonSerializer.Serialize(new
                {
                    type = "DATA_MEDIAPLAY",
                    source = sender.SourceAppUserModelId,
                    position = currentPosition
                });
                var json = rawJson;
                if (json != null)
                {
                    SendPlaybackData(json);
                }
            }
        }
        catch (COMException comEx)
        {
            // 忽略WinRT COM异常，避免频繁触发日志
            logger.LogDebug(comEx, "WinRT COM异常（时间线属性）：{SourceAppUserModelId}", sender.SourceAppUserModelId);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "处理时间线属性时出错：{SourceAppUserModelId}", sender.SourceAppUserModelId);
        }
    }

    private void UnsubscribeFromSessionEvents(GlobalSystemMediaTransportControlsSession session)
    {
        session.MediaPropertiesChanged -= Session_MediaPropertiesChanged;
        session.PlaybackInfoChanged -= Session_PlaybackInfoChanged;
        session.TimelinePropertiesChanged -= Session_TimelinePropertiesChanged;
        lastTimelinePosition.Remove(session.SourceAppUserModelId);

        if (!generalSettings.EnableSendMediaNotifications) return;

        // 发送媒体结束通知，使用与Android兼容的DATA_MEDIAPLAY格式
        foreach (var device in deviceManager.PairedDevices)
        {
            if (device.ConnectionStatus && device.DeviceSettings.MediaSessionSyncEnabled)
            {
                // 构造媒体结束包
                var rawJson = JsonSerializer.Serialize(new
                {
                    type = "DATA_MEDIAPLAY",
                    packageName = "MusicIsland",
                    appName = "Music Island",
                    title = "No media playing",
                    text = "",
                    coverUrl = "",
                    time = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                    isLocked = false,
                    mediaType = "FULL",
                    terminate = true,
                    terminateValue = "__END__",
                    featureKeyName = "si_feature_id",
                    featureKeyValue = "media_island_global"
                });
                string endPayloadJson = rawJson;
                if (endPayloadJson == null) continue;

                _ = protocolSender.SendMessageAsync(device.Id, endPayloadJson);
            }
        }
    }

    private async void Session_MediaPropertiesChanged(GlobalSystemMediaTransportControlsSession sender, MediaPropertiesChangedEventArgs args)
    {
        try
        {
            if (!generalSettings.EnableSendMediaNotifications) return;
            await UpdatePlaybackDataAsync(sender);

        }
        catch (COMException comEx)
        {
            logger.LogDebug(comEx, "WinRT COM异常（媒体属性变更）：{SourceAppUserModelId}", sender.SourceAppUserModelId);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "更新播放数据时出错：{SourceAppUserModelId}", sender.SourceAppUserModelId);
        }
    }

    private async void Session_PlaybackInfoChanged(GlobalSystemMediaTransportControlsSession sender, PlaybackInfoChangedEventArgs args)
    {
        try
        {
            if (!generalSettings.EnableSendMediaNotifications) return;
            await UpdatePlaybackDataAsync(sender);
        }
        catch (COMException comEx)
        {
            logger.LogDebug(comEx, "WinRT COM异常（播放信息变更）：{SourceAppUserModelId}", sender.SourceAppUserModelId);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "更新播放数据时出错：{SourceAppUserModelId}", sender.SourceAppUserModelId);
        }
    }

    private async Task UpdatePlaybackDataAsync(GlobalSystemMediaTransportControlsSession session)
    {
        try
        {
            await dispatcher.EnqueueAsync(async () =>
            {

                var playbackJson = await GetPlaybackSessionAsync(session);
                if (playbackJson is null || !activeSessions.ContainsKey(session.SourceAppUserModelId)) return;

                SendPlaybackData(playbackJson);
            });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "更新播放数据时出错：{SourceAppUserModelId}", session.SourceAppUserModelId);
        }
    }

    private async Task<string?> GetPlaybackSessionAsync(GlobalSystemMediaTransportControlsSession session)
    {
        try
        {
            // 只获取Android端需要的媒体字段
            var mediaProperties = await session.TryGetMediaPropertiesAsync();
            var timelineProperties = session.GetTimelineProperties();

            lastTimelinePosition[session.SourceAppUserModelId] = timelineProperties.Position.TotalMilliseconds;

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

            var rawJson = JsonSerializer.Serialize(new
            {
                type = "DATA_MEDIAPLAY",
                source = source,
                trackTitle = trackTitle,
                artist = artist,
                thumbnail = thumbnail,
                isPlaying = isPlaying
            });
            return rawJson;
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


    private void SendPlaybackData(string playbackJson)
    {
        if (!generalSettings.EnableSendMediaNotifications) return;

        try
        {
            using var doc = JsonDocument.Parse(playbackJson);
            var root = doc.RootElement;

            var source = root.TryGetProperty("source", out var srcProp) ? srcProp.GetString() : null;
            var trackTitle = root.TryGetProperty("trackTitle", out var titleProp) ? titleProp.GetString() : null;
            var artist = root.TryGetProperty("artist", out var artistProp) ? artistProp.GetString() : null;
            var thumbnail = root.TryGetProperty("thumbnail", out var thumbProp) ? thumbProp.GetString() : null;
            var isPlaying = root.TryGetProperty("isPlaying", out var playProp) && playProp.GetBoolean();

            // 生成唯一键，用于区分不同会话的不同消息类型
            string key = $"{source}|{(root.TryGetProperty("sessionType", out var stProp) ? stProp.GetString() : "default")}";

            // 检查是否需要节流
            if (lastSessionUpdateTime.TryGetValue(key, out var lastTime))
            {
                var elapsed = DateTime.Now - lastTime;
                if (elapsed.TotalMilliseconds < MinUpdateIntervalMs)
                {
                    return;
                }
            }

            lastSessionUpdateTime[key] = DateTime.Now;

            var currentState = new MediaPlayState
            {
                Title = trackTitle,
                Artist = artist,
                Thumbnail = thumbnail,
                SentTime = DateTime.Now
            };

            bool sendFullPayload = true;
            string title = trackTitle ?? string.Empty;
            string artistStr = artist ?? string.Empty;
            string text = string.Empty;
            string coverUrl = thumbnail ?? string.Empty;

            var sourceKey = source ?? string.Empty;
            if (lastMediaState.TryGetValue(sourceKey, out var oldState))
            {
                bool titleChanged = oldState.Title != currentState.Title;
                bool artistChanged = oldState.Artist != currentState.Artist;
                bool coverChanged = oldState.Thumbnail != currentState.Thumbnail;

                var now = DateTime.Now;
                sendFullPayload = coverChanged || (now - oldState.SentTime).TotalSeconds > 15;

                if (!sendFullPayload)
                {
                    if (!titleChanged) title = string.Empty;
                    if (!artistChanged) artistStr = string.Empty;
                    if (!coverChanged) coverUrl = string.Empty;
                }
            }

            lastMediaState[sourceKey] = currentState;

            if (!string.IsNullOrEmpty(title) && !string.IsNullOrEmpty(artistStr))
            {
                text = $"{artistStr} - {title}";
            }
            else if (!string.IsNullOrEmpty(title))
            {
                text = title;
            }
            else if (!string.IsNullOrEmpty(artistStr))
            {
                text = artistStr;
            }

            string appName = source ?? "Unknown App";

            foreach (var device in deviceManager.PairedDevices)
            {
                if (device.ConnectionStatus && device.DeviceSettings.MediaSessionSyncEnabled)
                {
                    var rawJson = JsonSerializer.Serialize(new
                    {
                        type = "DATA_MEDIAPLAY",
                        packageName = source,
                        appName = appName,
                        title = title,
                        text = text,
                        coverUrl = coverUrl,
                        time = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                        isLocked = false,
                        mediaType = sendFullPayload ? "FULL" : "DELTA"
                    });
                    string requestJson = rawJson;
                    if (requestJson == null) continue;
                    _ = protocolSender.SendMessageAsync(device.Id, requestJson);
                }
            }

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
                logger?.LogError(gamebarEx, "发送媒体信息到 Gamebar 失败");
            }

            // 向 VirtualSpeaker 转发歌曲信息
            if (virtualSpeaker.IsRunning && !string.IsNullOrEmpty(trackTitle))
            {
                try
                {
                    _ = virtualSpeaker.SendSongInfoAsync(
                        DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                        trackTitle ?? "",
                        artist ?? "",
                        0,
                        0,
                        ""
                    );
                }
                catch (Exception ssEx)
                {
                    logger.LogDebug(ssEx, "发送歌曲信息到 VirtualSpeaker 失败");
                }
            }
        }
        catch (JsonException)
        {
            logger.LogWarning("SendPlaybackData: 无法解析媒体播放数据 JSON");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "发送播放数据时出错");
        }
    }

    public Task HandleRemotePlaybackMessageAsync(string data)
    {
        throw new NotImplementedException();
    }

    public void GetAllAudioDevices()
    {
        try
        {
            // Get the default device
            var defaultDevice = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia).ID;

            // List all active devices
            var devices = enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active);
            foreach (var device in devices)
            {
                AudioDevices.Add(
                    new AudioDevice
                    {
                        DeviceId = device.ID,
                        DeviceName = device.FriendlyName,
                        Volume = device.AudioEndpointVolume.MasterVolumeLevelScalar,
                        IsMuted = device.AudioEndpointVolume.Mute,
                        IsSelected = device.ID == defaultDevice
                    }
                );
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "枚举音频设备失败");
        }
    }

    public void ToggleMute(string deviceId)
    {
        try
        {
            var endpoint = enumerator.GetDevice(deviceId);
            if (endpoint is null || endpoint.State != DeviceState.Active) return;

            try
            {
                endpoint.AudioEndpointVolume.Mute = !endpoint.AudioEndpointVolume.Mute;
            }
            catch (COMException comEx) when (comEx.HResult == unchecked((int)0x8007001F))
            {
                logger.LogWarning("设备 {DeviceId} 在静音时无法正常工作", deviceId);
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "静音设备 {DeviceId} 时出错", deviceId);
        }
    }

    public void SetVolume(string deviceId, float volume)
    {
        try
        {
            var endpoint = enumerator.GetDevice(deviceId);
            if (endpoint is null || endpoint.State is not DeviceState.Active) return;

            try
            {
                endpoint.AudioEndpointVolume.MasterVolumeLevelScalar = volume;
            }
            catch (COMException comEx) when (comEx.HResult == unchecked((int)0x8007001F))
            {
                logger.LogWarning("设备 {DeviceId} 在设置音量时无法正常工作", deviceId);
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "为设备 {DeviceId} 设置音量 {Volume} 时出错", volume, deviceId);
        }
    }


    public void SetDefaultAudioDevice(string deviceId)
    {
        object? policyConfigObject = null;
        try
        {
            Type? policyConfigType = Type.GetTypeFromCLSID(new Guid("870af99c-171d-4f9e-af0d-e63df40c2bc9"));
            if (policyConfigType is null) return;

            policyConfigObject = Activator.CreateInstance(policyConfigType);
            if (policyConfigObject is null) return;

            if (policyConfigObject is not IPolicyConfig policyConfig) return;

            int result1 = policyConfig.SetDefaultEndpoint(deviceId, ERole.eMultimedia);
            int result2 = policyConfig.SetDefaultEndpoint(deviceId, ERole.eCommunications);
            int result3 = policyConfig.SetDefaultEndpoint(deviceId, ERole.eConsole);

            if (result1 != HResult.S_OK || result2 != HResult.S_OK || result3 != HResult.S_OK)
            {
                logger.LogError("SetDefaultEndpoint 返回错误代码：{Result1}, {Result2}, {Result3}", result1, result2, result3);
                return;
            }

            var index = AudioDevices.FindIndex(d => d.DeviceId == deviceId);

            if (index != -1)
            {
                AudioDevices.First().IsSelected = false;
                AudioDevices[index].IsSelected = true;
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "设置默认设备时出错");
            return;
        }
        finally
        {
            if (policyConfigObject is not null)
            {
                Marshal.ReleaseComObject(policyConfigObject);
            }
        }
    }

    public void OnDeviceStateChanged(string deviceId, DeviceState newState)
    {
        logger.LogInformation("设备状态改变：{DeviceId} - {NewState}", deviceId, newState);
    }

    public void OnDeviceAdded(string pwstrDeviceId)
    {
        AudioDevices.Add(
            new AudioDevice
            {
                AudioDeviceType = AudioMessageType.New,
                DeviceId = pwstrDeviceId,
                DeviceName = enumerator.GetDevice(pwstrDeviceId).FriendlyName,
                Volume = enumerator.GetDevice(pwstrDeviceId).AudioEndpointVolume.MasterVolumeLevelScalar,
                IsMuted = enumerator.GetDevice(pwstrDeviceId).AudioEndpointVolume.Mute,
                IsSelected = false
            }
        );
        logger.LogInformation("设备已添加：{DeviceId}", pwstrDeviceId);
    }

    public void OnDeviceRemoved(string deviceId)
    {
        AudioDevices.RemoveAll(d => d.DeviceId == deviceId);
    }

    public void OnDefaultDeviceChanged(DataFlow flow, Role role, string defaultDeviceId)
    {
        var index = AudioDevices.FindIndex(d => d.DeviceId == defaultDeviceId);

        if (index != -1)
        {
            var selectedIndex = AudioDevices.FindIndex(d => d.IsSelected == true);
            AudioDevices[selectedIndex].IsSelected = false;
            AudioDevices[index].IsSelected = true;
            logger.LogInformation("默认设备已更改：{DefaultDeviceId}", defaultDeviceId);
        }
    }

    public void OnPropertyValueChanged(string pwstrDeviceId, PropertyKey key)
    {
        AudioDevice? device = AudioDevices.FirstOrDefault(d => d.DeviceId == pwstrDeviceId);
        device?.Volume = enumerator.GetDevice(pwstrDeviceId).AudioEndpointVolume.MasterVolumeLevelScalar;
    }

    /// <inheritdoc/>
    public void SendMediaControlRequest(string deviceId, string controlType)
    {
        var rawJson = JsonSerializer.Serialize(new
        {
            type = "DATA_MEDIA_CONTROL",
            action = controlType
        });
        string requestJson = rawJson;
        if (requestJson == null) return;
        _ = protocolSender.SendMessageAsync(deviceId, requestJson);
    }

    /// <summary>
    /// 发送媒体控制响应
    /// </summary>
    /// <param name="source">源</param>
    /// <param name="action">操作类型</param>
    /// <param name="success">是否成功</param>
    private void SendMediaControlResponse(string source, string action, bool success)
    {
        try
        {
            var rawJson = JsonSerializer.Serialize(new
            {
                type = "DATA_STATUS",
                originalHeader = "DATA_MEDIA_CONTROL",
                action = action,
                result = success ? "success" : "error",
                errorMessage = success ? string.Empty : "媒体操作失败"
            });
            string responseJson = rawJson;
            if (responseJson == null) return;

            foreach (var device in deviceManager.PairedDevices)
            {
                if (device.ConnectionStatus)
                {
                    _ = protocolSender.SendMessageAsync(device.Id, responseJson, "DATA_STATUS");
                }
            }

            logger.LogDebug("发送媒体控制响应: action={action}, result={result}", action, success ? "success" : "error");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "发送媒体控制响应时出错");
        }
    }

    /// <summary>
    /// 媒体控制响应类
    /// </summary>
    private class MediaControlResponse
    {
        [JsonPropertyName("originalHeader")]
        public string OriginalHeader { get; set; } = string.Empty;

        [JsonPropertyName("action")]
        public string Action { get; set; } = string.Empty;

        [JsonPropertyName("result")]
        public string Result { get; set; } = string.Empty;

        [JsonPropertyName("errorMessage")]
        public string ErrorMessage { get; set; } = string.Empty;
    }
}
