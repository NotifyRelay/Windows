using Microsoft.Extensions.Logging;
using NotifyRelay.Data.Contracts;
using NotifyRelay.Data.Models;
using NotifyRelay.Native;
using NotifyRelay.Services.Socket;

namespace NotifyRelay.Services;

/// <summary>
/// 服务端首行协议路由器
///
/// 所有协议消息统一走 Rust NativeCore.ProcessLine 分发：
/// - DATA 消息：Rust 解密后通过注册的 DATA 回调分发
/// - 非 DATA 消息：Rust 解码后通过注册的非 DATA 回调分发（携带结构化参数字段）
/// 回调执行期间通过 AsyncLocal 传递 TCP 会话上下文。
/// </summary>
public class ServerLineRouter
{
    private readonly ILogger<ServerLineRouter> _logger;

    public ServerLineRouter(
        ILogger<ServerLineRouter> logger)
    {
        _logger = logger;
    }

    public async Task RouteLineAsync(
        ServerSession session,
        string message,
        PairedDevice? device,
        NetworkService networkService)
    {
        NativeCore.CurrentSession.Value = session;
        try
        {
            var result = NativeCore.ProcessLine(message);
            if (result == -1)
            {
                _logger.LogWarning("processLine 处理失败: {Msg}", message);
            }
        }
        finally
        {
            NativeCore.CurrentSession.Value = null;
        }

        await Task.CompletedTask;
    }
}
