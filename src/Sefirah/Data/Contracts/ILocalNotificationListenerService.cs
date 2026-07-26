namespace NotifyRelay.Data.Contracts;

/// <summary>
/// 本地 Windows 通知监听服务接口
/// 使用 UserNotificationListener 捕获系统 Toast 通知
/// </summary>
public interface ILocalNotificationListenerService
{
    /// <summary>
    /// 启动监听
    /// </summary>
    void Start();

    /// <summary>
    /// 停止监听
    /// </summary>
    void Stop();

    /// <summary>
    /// 释放资源
    /// </summary>
    void Dispose();
}
