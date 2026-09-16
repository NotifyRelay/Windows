using System.Runtime.InteropServices;
using NotifyRelay.Data.Contracts;
using NotifyRelay.Native;
using Windows.Media.Control;

namespace NotifyRelay.Platforms.Windows.Services;

public class WindowsPlaybackService(
    ILogger<WindowsPlaybackService> logger,
    IDeviceManager deviceManager,
    IProtocolSender protocolSender,
    AudioDeviceManager audioDeviceManager,
    SmtcSessionRegistry sessionRegistry,
    PlaybackDataSyncer playbackDataSyncer,
    MediaControlExecutor mediaControlExecutor) : IPlaybackService
{
    /// <inheritdoc/>
    public async Task InitializeAsync()
    {
        try
        {
            // 先订阅 registry 事件，再触发首次会话同步：
            // 否则 SyncSessions 期间已建好的会话事件会因无人订阅而被丢弃
            sessionRegistry.MediaPropertiesChanged += OnSessionMediaPropertiesChanged;
            sessionRegistry.PlaybackInfoChanged += OnSessionPlaybackInfoChanged;
            sessionRegistry.SessionRemoved += OnSessionRemoved;

            if (!await sessionRegistry.InitializeAsync())
            {
                return;
            }

            audioDeviceManager.GetAllAudioDevices();

            // 启动音频设备监视器（失败不阻断初始化）
            audioDeviceManager.StartWatcher();

            // 注册媒体会话存在性查询（Rust 心跳查询回调 on_state_query 使用）：
            // 无活跃媒体会话时 Rust 移除媒体发送会话，避免接收端持续收到陈旧全量
            NativeCore.MediaSessionQueryHandler = _ => sessionRegistry.Count > 0;

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
        var result = await mediaControlExecutor.ExecuteAsync(mediaActionJson);
        if (result.Ignored) return;

        // 发送媒体操作响应
        SendMediaControlResponse(result.ActionType, result.Success);
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
        var requestJson = JsonSerializer.Serialize(new
        {
            type = "DATA_MEDIA_CONTROL",
            action = controlType
        });
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
            var responseJson = JsonSerializer.Serialize(new
            {
                type = "DATA_STATUS",
                originalHeader = "DATA_MEDIA_CONTROL",
                action = action,
                result = success ? "success" : "error",
                errorMessage = success ? string.Empty : "媒体操作失败"
            });

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
