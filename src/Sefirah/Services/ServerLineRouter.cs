using NotifyRelay.Data.Models;
using NotifyRelay.Services.Socket;

namespace NotifyRelay.Services;

/// <summary>
/// 服务端首行协议路由器
///
/// 职责：
/// - 解析 TCP 首行协议前缀并分发到对应处理器
/// - HANDSHAKE → NetworkService 握手处理
/// - DATA_* → ProtocolRouter 加密业务通道
/// - HEARTBEAT_TCP → HeartbeatProcessor（待迁移）
/// - 其他 → 断开连接
/// </summary>
public class ServerLineRouter
{
    private readonly ILogger<ServerLineRouter> _logger;

    public ServerLineRouter(
        ILogger<ServerLineRouter> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// 分发首行协议到对应处理器
    /// </summary>
    /// <param name="session">TCP 会话</param>
    /// <param name="message">首行协议文本</param>
    /// <param name="device">已绑定的设备（null 表示尚未握手）</param>
    /// <param name="networkService">NetworkService 实例，用于执行具体操作</param>
    public async Task RouteLineAsync(
        ServerSession session,
        string message,
        PairedDevice? device,
        NetworkService networkService)
    {
        if (device == null)
        {
            await RouteUnboundAsync(session, message, networkService);
        }
        else
        {
            await networkService.ProcessProtocolMessageAsync(device, message);
        }
    }

    private async Task RouteUnboundAsync(
        ServerSession session,
        string message,
        NetworkService networkService)
    {
        if (message.StartsWith("HANDSHAKE:"))
        {
            await networkService.HandleHandshakeAsync(session, message);
        }
        else if (message.StartsWith("DATA_"))
        {
            var attachedDevice = await networkService.TryAttachExistingDeviceSessionAsync(session, message);
            if (attachedDevice != null)
            {
                await networkService.ProcessProtocolMessageAsync(attachedDevice, message);
            }
            else
            {
                _logger.LogWarning("收到未预期的 DATA 消息，来源: {id}", session.Id);
                networkService.DisconnectSession(session);
            }
        }
        else if (message.StartsWith("HEARTBEAT_TCP:"))
        {
            await networkService.ProcessProtocolMessageAsync(null!, message);
        }
        else
        {
            _logger.LogWarning("收到未预期的预握手消息，来源: {id}，消息: {msg}",
                session.Id, message.Length > 50 ? message[..50] + "..." : message);
            networkService.DisconnectSession(session);
        }
    }
}
