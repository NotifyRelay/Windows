using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using CommunityToolkit.WinUI;
using Microsoft.UI.Dispatching;
using NotifyRelay.Data.Contracts;
using NotifyRelay.Data.Enums;
using NotifyRelay.Data.Models;
using NotifyRelay.Helpers;
using NotifyRelay.Native;
using NotifyRelay.Services.Devices;
using NotifyRelay.Services.Media;
using NotifyRelay.Services.Protocol;
using NotifyRelay.Services.Infrastructure;
using NotifyRelay.Services.Overlay;
using Windows.Media;
using Windows.Media.Control;
using Windows.System;

namespace NotifyRelay.Platforms.Windows.Services;

public class WindowsPlaybackService(
    ILogger<WindowsPlaybackService> logger,
    ISessionManager sessionManager,
    IDeviceManager deviceManager,
    IProtocolSender protocolSender,
    AudioDeviceManager audioDeviceManager,
    SmtcSessionRegistry sessionRegistry,
    PlaybackDataSyncer playbackDataSyncer) : IPlaybackService
{
    private readonly Microsoft.UI.Dispatching.DispatcherQueue dispatcher = Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread();

    /// <inheritdoc/>
    public async Task InitializeAsync()
    {
        try
        {
            if (!await sessionRegistry.InitializeAsync())
            {
                return;
            }

            // 订阅会话事件：媒体属性/播放状态变更触发播放数据刷新，会话移除触发卡片清理与结束标记
            sessionRegistry.MediaPropertiesChanged += OnSessionMediaPropertiesChanged;
            sessionRegistry.PlaybackInfoChanged += OnSessionPlaybackInfoChanged;
            sessionRegistry.SessionRemoved += OnSessionRemoved;

            audioDeviceManager.GetAllAudioDevices();

            // 启动音频设备监视器（失败不阻断初始化）
            audioDeviceManager.StartWatcher();

            // 注册媒体会话存在性查询（Rust 心跳查询回调 on_state_query 使用）：
            // 无活跃媒体会话时 Rust 移除媒体发送会话，避免接收端持续收到陈旧全量
            NativeCore.MediaSessionQueryHandler = _ => sessionRegistry.Count > 0;

            sessionManager.ConnectionStatusChanged += async (sender, args) =>
            {
                await Task.CompletedTask;
            };

            // 启动 9 秒周期的媒体状态重推循环
            playbackDataSyncer.StartPeriodicSyncLoop();

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
                return;
            }
        }

        // 执行媒体操作并发送响应
        bool success = await ExecuteSessionActionAsync(session, source, actionType, value);

        // 发送媒体操作响应
        SendMediaControlResponse(actionType ?? string.Empty, success);
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

    private async void OnSessionMediaPropertiesChanged(object? sender, GlobalSystemMediaTransportControlsSession session)
    {
        try
        {
            await playbackDataSyncer.UpdatePlaybackDataAsync(session);
        }
        catch (COMException comEx)
        {
            logger.LogDebug(comEx, "WinRT COM异常（媒体属性变更）：{SourceAppUserModelId}", session.SourceAppUserModelId);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "更新播放数据时出错：{SourceAppUserModelId}", session.SourceAppUserModelId);
        }
    }

    private async void OnSessionPlaybackInfoChanged(object? sender, GlobalSystemMediaTransportControlsSession session)
    {
        try
        {
            await playbackDataSyncer.UpdatePlaybackDataAsync(session);
        }
        catch (COMException comEx)
        {
            logger.LogDebug(comEx, "WinRT COM异常（播放信息变更）：{SourceAppUserModelId}", session.SourceAppUserModelId);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "更新播放数据时出错：{SourceAppUserModelId}", session.SourceAppUserModelId);
        }
    }

    private void OnSessionRemoved(object? sender, string appUserModelId)
    {
        playbackDataSyncer.HandleSessionRemoved(appUserModelId);
    }

    public Task HandleRemotePlaybackMessageAsync(string data)
    {
        throw new NotImplementedException();
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
    private void SendMediaControlResponse(string action, bool success)
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
}
