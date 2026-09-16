using CommunityToolkit.WinUI;
using Microsoft.UI.Dispatching;
using Windows.Media;
using Windows.Media.Control;

namespace NotifyRelay.Platforms.Windows.Services;

/// <summary>入站媒体控制指令的执行结果。</summary>
/// <param name="Ignored">为 true 表示指令被忽略（未解析出 source，或指向本应用自身会话），调用方不应发送响应。</param>
/// <param name="ActionType">解析出的操作类型，用于构造响应。</param>
/// <param name="Success">操作是否执行成功。</param>
public sealed record MediaControlExecutionResult(bool Ignored, string ActionType, bool Success);

/// <summary>
/// 入站远程媒体控制指令的解析与执行。
/// 负责按来源选择 SMTC 会话，并把播放类指令交给 SMTC、音频类指令交给 AudioDeviceManager。
/// </summary>
public class MediaControlExecutor(
    ILogger<MediaControlExecutor> logger,
    AudioDeviceManager audioDeviceManager,
    SmtcSessionRegistry sessionRegistry)
{
    private readonly Microsoft.UI.Dispatching.DispatcherQueue dispatcher = Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread();

    /// <summary>
    /// 解析并执行远端下发的媒体控制指令。
    /// </summary>
    /// <param name="mediaActionJson">媒体控制指令 JSON。</param>
    /// <returns>执行结果；<see cref="MediaControlExecutionResult.Ignored"/> 为 true 时调用方不应发送响应。</returns>
    public async Task<MediaControlExecutionResult> ExecuteAsync(string mediaActionJson)
    {
        var (source, actionType, value) = ParseMediaActionData(mediaActionJson);
        if (source == null) return new MediaControlExecutionResult(true, string.Empty, false);

        // 尝试根据Source字段查找对应的媒体会话
        GlobalSystemMediaTransportControlsSession? session = null;
        bool found = sessionRegistry.TryGetBySource(source, out session);

        // 如果找不到匹配的会话，或者Source是"MediaControl"（来自外部设备的控制指令），则使用当前活动的媒体会话
        if (!found || session is null || source == "MediaControl")
        {
            session = sessionRegistry.CurrentSession;
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
                return new MediaControlExecutionResult(true, string.Empty, false);
            }
        }

        // 执行媒体操作
        bool success = await ExecuteSessionActionAsync(session, source, actionType, value);

        return new MediaControlExecutionResult(false, actionType ?? string.Empty, success);
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
                        audioDeviceManager.SetDefaultAudioDevice(source ?? string.Empty);
                        success = true;
                        break;
                    case "VolumeUpdate":
                        if (value.HasValue)
                        {
                            audioDeviceManager.SetVolume(source ?? string.Empty, Convert.ToSingle(value.Value));
                            success = true;
                        }
                        break;
                    case "ToggleMute":
                        audioDeviceManager.ToggleMute(source ?? string.Empty);
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
}
