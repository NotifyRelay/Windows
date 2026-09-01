using NotifyRelay.Data.Models;

namespace NotifyRelay.Data.Contracts;

/// <summary>
/// 统一加密发送器接口
/// </summary>
public interface IProtocolSender
{
    /// <summary>
    /// 发送消息，自动从 JSON 中提取 type 作为协议头
    /// </summary>
    Task SendMessageAsync(string deviceId, string messageJson);

    /// <summary>
    /// 发送消息，指定协议头
    /// </summary>
    Task SendMessageAsync(string deviceId, string messageJson, string header);

    /// <summary>
    /// 发送加密负载到指定设备
    /// </summary>
    Task SendEncryptedAsync(PairedDevice device, string header, string plaintext, string localDeviceId, string localPublicKey, int timeoutMs = 80000);

    /// <summary>
    /// 发送加密负载到指定设备（默认超时）
    /// </summary>
    Task SendEncryptedAsync(PairedDevice device, string header, string plaintext, string localDeviceId, string localPublicKey);
}
