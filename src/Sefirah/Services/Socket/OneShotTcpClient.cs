using NotifyRelay.Native;

namespace NotifyRelay.Services.Socket;

/// <summary>
/// 一次性 TCP 客户端工具类
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
        IntPtr ctx,
        string ip,
        int port,
        string payload,
        int connectTimeoutMs = (int)DefaultConnectTimeout,
        int timeoutMs = (int)DefaultTimeout)
    {
        var result = NotifyRelayCore.Safe.OneShotSendReceive(ctx, ip, (ushort)port, payload, (uint)connectTimeoutMs);
        // 在新型架构中，oneshot 返回 Int，响应已通过 process_line 内部处理
        return Task.FromResult(result == 0 ? "" : null);
    }

    /// <summary>
    /// 仅发送报文，不等待响应。
    /// </summary>
    public static Task<bool> SendOnlyAsync(
        IntPtr ctx,
        string ip,
        int port,
        string payload,
        int connectTimeoutMs = (int)DefaultConnectTimeout,
        int timeoutMs = (int)DefaultTimeout)
    {
        var result = NotifyRelayCore.Safe.OneShotSendOnly(ctx, ip, (ushort)port, payload, (uint)connectTimeoutMs);
        return Task.FromResult(result == 0);
    }
}
