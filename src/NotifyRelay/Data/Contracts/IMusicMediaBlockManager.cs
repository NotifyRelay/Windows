using NotifyRelay.Data.Models;

namespace NotifyRelay.Data.Contracts;

/// <summary>
/// 音乐媒体块管理服务：媒体块生命周期、超时检查与封面转换。
/// </summary>
public interface IMusicMediaBlockManager
{
    /// <summary>
    /// 当前显示的音乐媒体块列表（只读，支持多个设备同时显示）
    /// </summary>
    ReadOnlyObservableCollection<MusicMediaBlock> Blocks { get; }

    /// <summary>
    /// 启动 1 秒周期的超时检查定时器（由 NotificationService.Initialize 调用）
    /// </summary>
    void StartTimeoutChecker();

    /// <summary>
    /// 处理媒体播放通知
    /// </summary>
    Task HandleMediaPlayNotification(PairedDevice device, string payload);

    /// <summary>
    /// 处理媒体播放消息 (DATA_MEDIAPLAY)
    /// </summary>
    Task ProcessMediaPlayMessageAsync(PairedDevice device, string payload);

    /// <summary>
    /// 检查音乐媒体块是否超时
    /// </summary>
    void CheckMusicMediaBlockTimeout();
}
