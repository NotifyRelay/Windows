using NotifyRelay.Data.Contracts;
using NotifyRelay.Data.Models;
using NotifyRelay.Native;
using NotifyRelay.Services.Socket;

namespace NotifyRelay.Services;

/// <summary>
/// 服务端首行协议路由器
///
/// 职责：
/// - 解析 TCP 首行协议前缀并分发到对应处理器
/// - HANDSHAKE → NetworkService 握手处理
/// - DATA_* → ProtocolRouter 加密业务通道
/// - HEARTBEAT_TCP → HeartbeatProcessor 统一处理
/// - 其他 → 断开连接
/// </summary>
public class ServerLineRouter
{
    private readonly ILogger<ServerLineRouter> _logger;
    private readonly HeartbeatProcessor _heartbeatProcessor;
    private readonly IDeviceManager _deviceManager;

    public ServerLineRouter(
        ILogger<ServerLineRouter> logger,
        HeartbeatProcessor heartbeatProcessor,
        IDeviceManager deviceManager)
    {
        _logger = logger;
        _heartbeatProcessor = heartbeatProcessor;
        _deviceManager = deviceManager;
    }

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
        var jsonStr = NativeCore.DecodeLine(message);
        if (jsonStr == null)
        {
            // decodeLine 无法识别的消息（如 HEARTBEAT_TCP、NOTIFYRELAY_DISCOVER_MANUAL）
            await HandleUnrecognizedAsync(session, message, networkService);
            return;
        }

        using var doc = System.Text.Json.JsonDocument.Parse(jsonStr);
        var root = doc.RootElement;
        var header = root.GetProperty("header").GetString();

        _logger.LogDebug("routeLine: header={Header}", header);

        // DATA_* 消息：由 Rust 解密后直接处理明文
        if (root.TryGetProperty("type", out var typeProp) && typeProp.GetString() == "data")
        {
            var localUuid = root.GetProperty("local_uuid").GetString() ?? "";
            var plaintext = root.GetProperty("plaintext").GetString() ?? "";
            var attachedDevice = _deviceManager.PairedDevices.FirstOrDefault(d => d.Id == localUuid);
            if (attachedDevice != null)
            {
                await networkService.ProcessDecryptedDataAsync(attachedDevice, header ?? "", plaintext);
            }
            else
            {
                networkService.DisconnectSession(session);
            }
            return;
        }

        // 配对/握手消息：传递 JSON 字符串给 handler
        switch (header)
        {
            case "PAIRING_INIT":
                await networkService.HandlePairingInitAsync(session, jsonStr);
                break;
            case "PAIRING_RESP":
                await networkService.HandlePairingRespAsync(session, jsonStr);
                break;
            case "ACCEPT":
                await networkService.HandlePairingAcceptAsync(session, jsonStr);
                break;
            case "HANDSHAKE":
                await networkService.HandleHandshakeAsync(session, jsonStr);
                break;
            case "HEARTBEAT_TCP":
                await HandleUnrecognizedAsync(session, message, networkService);
                break;
            default:
                _logger.LogWarning("收到未预期的协议消息: {Header}", header);
                networkService.DisconnectSession(session);
                break;
        }
    }

    private async Task HandleUnrecognizedAsync(
        ServerSession session,
        string message,
        NetworkService networkService)
    {
        if (message.StartsWith("HEARTBEAT_TCP:"))
        {
            var processed = _heartbeatProcessor.TryProcessHeartbeat(message, null, d =>
            {
                d.LastHeartbeat = DateTime.UtcNow;
            });
            if (!processed)
            {
                _logger.LogDebug("HEARTBEAT_TCP 未找到已配对设备，忽略");
            }
            networkService.DisconnectSession(session);
        }
        else
        {
            _logger.LogWarning("收到未识别的预握手消息，来源: {id}，消息: {msg}",
                session.Id, message.Length > 50 ? message[..50] + "..." : message);
            networkService.DisconnectSession(session);
        }
    }
}
