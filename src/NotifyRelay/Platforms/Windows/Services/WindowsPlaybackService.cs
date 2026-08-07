using System.Runtime.InteropServices;
using CommunityToolkit.WinUI;
using Microsoft.UI.Dispatching;
using NAudio.CoreAudioApi;
using NAudio.Utils;
using NotifyRelay.Data.Contracts;
using NotifyRelay.Data.Enums;
using NotifyRelay.Data.Models;
using NotifyRelay.Helpers;
using NotifyRelay.Native;
using NotifyRelay.Platforms.Windows.Interop;
using NotifyRelay.Services;
using NotifyRelay.Services.Overlay;
using Windows.Media;
using Windows.Media.Control;
using Windows.Devices.Enumeration;
using Windows.Media.Devices;

namespace NotifyRelay.Platforms.Windows.Services;

public class WindowsPlaybackService(
    ILogger<WindowsPlaybackService> logger,
    ISessionManager sessionManager,
    IDeviceManager deviceManager,
    IProtocolSender protocolSender,
    IGeneralSettingsService generalSettings) : IPlaybackService
{
    private readonly DispatcherQueue dispatcher = DispatcherQueue.GetForCurrentThread();
    private readonly Dictionary<string, GlobalSystemMediaTransportControlsSession> activeSessions = [];
    private GlobalSystemMediaTransportControlsSessionManager? manager;

    // Local SMTC for remote media display

    public List<AudioDevice> AudioDevices { get; private set; } = [];
    private readonly MMDeviceEnumerator enumerator = new();

    private readonly Dictionary<string, double> lastTimelinePosition = [];

    // WinRT device watcher for audio endpoint changes
    private DeviceWatcher? deviceWatcher;

    // 媒体播放状态跟踪已移至 Rust 合并引擎（由 PushMediaState 推送全量，Rust 负责 diff）。

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

            // Use WinRT DeviceWatcher to monitor audio device add/remove/update events.
            try
            {
                deviceWatcher = DeviceInformation.CreateWatcher(MediaDevice.GetAudioRenderSelector());
                deviceWatcher.Added += DeviceWatcher_Added;
                deviceWatcher.Removed += DeviceWatcher_Removed;
                deviceWatcher.Updated += DeviceWatcher_Updated;
                deviceWatcher.EnumerationCompleted += DeviceWatcher_EnumerationCompleted;
                deviceWatcher.Start();

                // Subscribe to default audio render device changes and update selection accordingly.
                MediaDevice.DefaultAudioRenderDeviceChanged += MediaDevice_DefaultAudioRenderDeviceChanged;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "无法启动设备监视器，回退到手动/定期刷新");
            }

            manager.SessionsChanged += SessionsChanged;

            // 注册媒体会话存在性查询（Rust 心跳查询回调 on_state_query 使用）：
            // 无活跃媒体会话时 Rust 移除媒体发送会话，避免接收端持续收到陈旧全量
            NativeCore.MediaSessionQueryHandler = _ =>
            {
                lock (activeSessions)
                {
                    return activeSessions.Count > 0;
                }
            };

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
        SendMediaControlResponse(source, actionType ?? string.Empty, success);
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
                        SetDefaultAudioDevice(source ?? string.Empty);
                        success = true;
                        break;
                    case "VolumeUpdate":
                        if (value.HasValue)
                        {
                            SetVolume(source ?? string.Empty, Convert.ToSingle(value.Value));
                            success = true;
                        }
                        break;
                    case "ToggleMute":
                        ToggleMute(source ?? string.Empty);
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

                // 时间线位置变化不再单独发送：全量媒体状态由 Rust 合并引擎统一推送（见 SendPlaybackData）。
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

        // 会话真正结束时移除 Overlay 媒体卡片
        if (generalSettings.DanmakuMediaCardEnabled)
        {
            try
            {
                var overlay = Ioc.Default.GetRequiredService<OverlayRenderService>();
                overlay.RemoveMediaCard(session.SourceAppUserModelId ?? "local");
                logger?.LogDebug("UnsubscribeFromSessionEvents: 移除媒体卡片 source={Source}", session.SourceAppUserModelId);
            }
            catch (Exception ex)
            {
                logger?.LogError(ex, "移除 Overlay 媒体卡片失败");
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
        try
        {
            using var doc = JsonDocument.Parse(playbackJson);
            var root = doc.RootElement;

            var source = root.TryGetProperty("source", out var srcProp) ? srcProp.GetString() : null;
            var trackTitle = root.TryGetProperty("trackTitle", out var titleProp) ? titleProp.GetString() : null;
            var artist = root.TryGetProperty("artist", out var artistProp) ? artistProp.GetString() : null;
            var thumbnail = root.TryGetProperty("thumbnail", out var thumbProp) ? thumbProp.GetString() : null;
            var isPlaying = root.TryGetProperty("isPlaying", out var playProp) && playProp.GetBoolean();

            logger?.LogDebug("SendPlaybackData: source={Source}, trackTitle={Title}, artist={Artist}, isPlaying={IsPlaying}",
                source, trackTitle, artist, isPlaying);

            // Overlay 显示独立于远程发送开关
            var overlayEnabled = generalSettings.DanmakuMediaCardEnabled;
            var forceGamebar = generalSettings.GamebarRelayEnabled;

            // 远程推送（受 EnableSendMediaNotifications 控制）
            // 推送「全量」媒体状态；差异计算（FULL/DELTA）与合并由 Rust 合并引擎负责。
            bool shouldSendRemote = generalSettings.EnableSendMediaNotifications;
            if (shouldSendRemote)
            {
                string mediaJson = JsonSerializer.Serialize(new
                {
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
                    // 会话真正结束时会在 UnsubscribeFromSessionEvents 中移除卡片
                    if (string.IsNullOrEmpty(trackTitle) && string.IsNullOrEmpty(artist))
                    {
                        logger?.LogDebug("SendPlaybackData: 跳过空媒体数据，保留当前卡片 source={Source}", source);
                    }
                    else
                    {
                        byte[]? coverBytes = null;
                        if (!string.IsNullOrEmpty(thumbnail))
                        {
                            coverBytes = ConvertBase64ToBytes(thumbnail);
                        }
                        var overlay = Ioc.Default.GetRequiredService<OverlayRenderService>();
                        logger?.LogDebug("SendPlaybackData: 显示媒体卡片 source={Source}, title={Title}", source, trackTitle);
                        overlay.ShowMediaCard(source ?? "local", "本机", trackTitle ?? "", artist ?? "", coverBytes, isPlaying);
                    }
                }
                catch (Exception ex)
                {
                    logger?.LogError(ex, "发送媒体信息到 Overlay 失败");
                }
            }

            if (forceGamebar || !overlayEnabled)
            {
                // 空值表示"未改变"，跳过发送，避免 Gamebar 端闪烁
                if (string.IsNullOrEmpty(trackTitle) && string.IsNullOrEmpty(artist))
                {
                    logger?.LogDebug("SendPlaybackData: 跳过 Gamebar 空媒体数据 source={Source}", source);
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
                        logger?.LogError(gamebarEx, "发送媒体信息到 Gamebar 失败");
                    }
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

    private static byte[]? ConvertBase64ToBytes(string? base64)
    {
        if (string.IsNullOrEmpty(base64)) return null;
        try
        {
            // Handle data URI format
            if (base64.Contains(','))
                base64 = base64.Split(',')[1];
            return Convert.FromBase64String(base64);
        }
        catch { return null; }
    }

    public Task HandleRemotePlaybackMessageAsync(string data)
    {
        throw new NotImplementedException();
    }

    public void GetAllAudioDevices()
    {
        try
        {
            AudioDevices.Clear();

            // Get the default device ID (NAudio)
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
        // 保留旧方法以供兼容，但实际由 DeviceWatcher 触发时会整体刷新设备列表
        GetAllAudioDevices();
        logger.LogInformation("设备已添加：{DeviceId}", pwstrDeviceId);
    }

    public void OnDeviceRemoved(string deviceId)
    {
        // 由 DeviceWatcher 触发时整体刷新设备列表以保持一致性
        GetAllAudioDevices();
    }

    public void OnDefaultDeviceChanged(DataFlow flow, Role role, string defaultDeviceId)
    {
        // 旧回调兼容实现：尝试设置选中项
        var index = AudioDevices.FindIndex(d => d.DeviceId == defaultDeviceId);

        if (index != -1)
        {
            var selectedIndex = AudioDevices.FindIndex(d => d.IsSelected == true);
            if (selectedIndex != -1)
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

    // WinRT DeviceWatcher / MediaDevice 事件处理，替代 IMMNotificationClient 回调
    private void DeviceWatcher_Added(DeviceWatcher sender, DeviceInformation args)
    {
        _ = dispatcher.EnqueueAsync(() =>
        {
            try
            {
                GetAllAudioDevices();
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "处理设备添加事件时出错");
            }
        });
    }

    private void DeviceWatcher_Removed(DeviceWatcher sender, DeviceInformationUpdate args)
    {
        _ = dispatcher.EnqueueAsync(() =>
        {
            try
            {
                GetAllAudioDevices();
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "处理设备移除事件时出错");
            }
        });
    }

    private void DeviceWatcher_Updated(DeviceWatcher sender, DeviceInformationUpdate args)
    {
        _ = dispatcher.EnqueueAsync(() =>
        {
            try
            {
                GetAllAudioDevices();
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "处理设备更新事件时出错");
            }
        });
    }

    private void DeviceWatcher_EnumerationCompleted(DeviceWatcher sender, object args)
    {
        _ = dispatcher.EnqueueAsync(() => { logger.LogDebug("设备枚举完成"); });
    }

    private void MediaDevice_DefaultAudioRenderDeviceChanged(object sender, DefaultAudioRenderDeviceChangedEventArgs args)
    {
        _ = dispatcher.EnqueueAsync(() =>
        {
            try
            {
                UpdateDefaultSelection();
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "处理默认设备更改时出错");
            }
        });
    }

    private void UpdateDefaultSelection()
    {
        try
        {
            var defaultDevice = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
            if (defaultDevice != null)
            {
                var id = defaultDevice.ID;
                var index = AudioDevices.FindIndex(d => d.DeviceId == id);
                if (index != -1)
                {
                    var selectedIndex = AudioDevices.FindIndex(d => d.IsSelected == true);
                    if (selectedIndex != -1)
                        AudioDevices[selectedIndex].IsSelected = false;
                    AudioDevices[index].IsSelected = true;
                    logger.LogInformation("默认设备已更改：{DefaultDeviceId}", id);
                }
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "更新默认音频设备选择时出错");
        }
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
