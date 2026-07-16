using NotifyRelay.Native;

namespace NotifyRelay.Services.Socket;

/// <summary>
/// 一次性 TCP 客户端工具类
///
/// 封装「创建连接 → 发送报文 → 读取响应 → 关闭」的模板逻辑，
/// 消除 ProtocolSender 中的重复 Socket 代码。
///
/// 实际网络操作委托给 Rust Core (nrc_oneshot_send_receive / nrc_oneshot_send_only)。
/// </summary>
public static class OneShotTcpClient
{
    private const uint DefaultConnectTimeout = 5000;
    private const uint DefaultTimeout = 80000;

    /// <summary>
    /// 发送报文并返回完整响应。
    /// </summary>
    public static Task<string?> SendAndReceiveAsync(
        string ip,
        int port,
        string payload,
        int connectTimeoutMs = (int)DefaultConnectTimeout,
        int timeoutMs = (int)DefaultTimeout)
    {
        var result = NotifyRelayCore.Safe.OneShotSendReceive(ip, (ushort)port, payload, (uint)connectTimeoutMs, (uint)timeoutMs);
        return Task.FromResult(result);
    }

    /// <summary>
    /// 仅发送报文，不等待响应。
    /// </summary>
    public static Task<bool> SendOnlyAsync(
        string ip,
        int port,
        string payload,
        int connectTimeoutMs = (int)DefaultConnectTimeout,
        int timeoutMs = (int)DefaultTimeout)
    {
        var result = NotifyRelayCore.Safe.OneShotSendOnly(ip, (ushort)port, payload, (uint)connectTimeoutMs);
        return Task.FromResult(result != 0);
    }
}
