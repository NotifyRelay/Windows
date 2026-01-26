using NotifyRelay.Data.Contracts;
using NotifyRelay.Data.Models;

namespace NotifyRelay.Platforms.Desktop.Services;
public class DesktopPlaybackService : IPlaybackService
{
    public Task HandleMediaActionAsync(PlaybackAction mediaAction)
    {
        return Task.CompletedTask;
    }

    public Task HandleRemotePlaybackMessageAsync(PlaybackSession data)
    {
        return Task.CompletedTask;
    }

    public Task InitializeAsync()
    {
        return Task.CompletedTask;
    }

    public void SendMediaControlRequest(string deviceId, string controlType)
    {
        // 桌面平台实现暂为空，或者记录日志
    }
}
