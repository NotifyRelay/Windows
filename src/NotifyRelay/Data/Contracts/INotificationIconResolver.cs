using NotifyRelay.Data.Models;

namespace NotifyRelay.Data.Contracts;

/// <summary>
/// 应用图标解析服务：图标请求/响应、缓存刷新与图标编码。
/// </summary>
public interface INotificationIconResolver
{
    /// <summary>
    /// 本地无图标时发送请求并等待响应，最长 ICON_REQUEST_TIMEOUT 毫秒
    /// </summary>
    Task WaitForIconAsync(string deviceId, string appPackage);

    /// <summary>
    /// 读取本地图标文件，一次性返回原始字节（供叠加层弹幕使用）与 data URL（供 TCP 转发使用）；
    /// 本地无图标时两者均为 null。
    /// </summary>
    Task<(byte[]? Bytes, string? DataUrl)> LoadIconAsync(string appPackage);

    /// <summary>
    /// 解析 DATA_ICON_RESPONSE，落盘图标并刷新相关通知
    /// </summary>
    Task ProcessIconResponseAsync(PairedDevice device, string payload);

    /// <summary>
    /// 刷新指定包名下所有通知的图标并请求重建分组
    /// </summary>
    Task RefreshNotificationIconsAsync(string packageName);

    /// <summary>
    /// 注入通知集合访问器与重建回调，避免 Resolver 反向依赖 NotificationService
    /// </summary>
    void Configure(Func<IEnumerable<Notification>> notificationsProvider, Action rebuildCallback);
}
