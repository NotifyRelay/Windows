using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using NetCoreServer;
using NotifyRelay.Services.Socket;
using SocketError = System.Net.Sockets.SocketError;
using TcpSession = NetCoreServer.TcpSession;

namespace NotifyRelay.Services;

/// <summary>
/// Local TCP server (backed by NetCoreServer) for delivering notifications to UWP widget clients.
/// Messages are sent as UTF-8 JSON lines delimited by '\n'.
/// </summary>
public static class LocalSocketRelayServer
{
    private static Server? _server;
    private static RelayProvider? _provider;
    private static ILogger? logger;

    // 事件定义：当收到客户端指令时触发
    public static event EventHandler<string>? CommandReceived;

    public static bool IsRunning => _server != null;

    /// <summary>
    /// Sets the logger for the LocalSocketRelayServer.
    /// </summary>
    public static void SetLogger(ILogger loggerInstance)
    {
        logger = loggerInstance;
    }

    public static void Start(int port = 45678)
    {
        try
        {
            if (_server != null) return;
            _provider = new RelayProvider();
            _server = new Server(IPAddress.Loopback, port, _provider, logger ?? NullLogger.Instance);
            _server.Start();
            logger?.LogInformation($"本地Socket中继服务器: 已在 127.0.0.1:{port} 启动");
        }
        catch (Exception ex)
        {
            logger?.LogError(ex, "本地Socket中继服务器: 启动失败");
        }
    }

    public static void Stop()
    {
        try
        {
            _server?.Stop();
            _server?.Dispose();
            _server = null;
            _provider = null;
            logger?.LogInformation("本地Socket中继服务器: 已停止");
        }
        catch (Exception ex)
        {
            logger?.LogError(ex, "本地Socket中继服务器: 停止错误");
        }
    }

    private static List<TcpSession> GetConnectedSessions()
    {
        var sessions = _provider?.Sessions.Where(s => s.IsConnected).ToList() ?? [];
        logger?.LogInformation("本地Socket中继服务器: 实际处理 {ClientCount} 个客户端", sessions.Count);
        return sessions;
    }

    private static bool SendToAll(string json)
    {
        var payload = json + "\n";
        var data = Encoding.UTF8.GetBytes(payload);
        bool sentToAnyClient = false;
        int sentCount = 0;
        int failedCount = 0;

        var sessions = GetConnectedSessions();

        foreach (var session in sessions)
        {
            if (session.SendAsync(data))
            {
                sentToAnyClient = true;
                sentCount++;
            }
            else
            {
                failedCount++;
            }
        }

        logger?.LogInformation("本地Socket中继服务器: 通知发送结果 - 总数: {ClientListCount}, 成功: {SentCount}, 失败: {FailedCount}",
            sessions.Count, sentCount, failedCount);

        return sentToAnyClient;
    }

    public static Task<bool> SendNotificationAsync(string appName, string packageName, string title, string body, string? iconUrl = null, string? deviceName = null)
    {
        try
        {
            // 创建通知对象
            var notification = new { appName, packageName, title, body, iconUrl, deviceName };
            // 序列化JSON，确保正确处理特殊字符
            var json = System.Text.Json.JsonSerializer.Serialize(notification);
            logger?.LogInformation("本地Socket中继服务器: 尝试向客户端发送通知");

            return Task.FromResult(SendToAll(json));
        }
        catch (System.Text.Json.JsonException ex)
        {
            logger?.LogError(ex, "本地Socket中继服务器: JSON序列化失败");
            return Task.FromResult(false);
        }
        catch (Exception ex)
        {
            logger?.LogError(ex, "本地Socket中继服务器: 发送通知时发生意外错误");
            return Task.FromResult(false);
        }
    }

    public static Task<bool> SendMediaInfoAsync(string deviceId, string deviceName, string title, string artist, string coverUrl, bool isPlaying)
    {
        try
        {
            var mediaInfo = new
            {
                type = "media_update",
                deviceId,
                deviceName,
                title,
                artist,
                coverUrl,
                isPlaying
            };

            var json = System.Text.Json.JsonSerializer.Serialize(mediaInfo);
            return Task.FromResult(SendToAll(json));
        }
        catch (Exception ex)
        {
            logger?.LogError(ex, "本地Socket中继服务器: 发送媒体信息失败");
            return Task.FromResult(false);
        }
    }

    public static Task<bool> SendSuperIslandAsync(
        string deviceId,
        string deviceName,
        string sourceId,
        bool isEnd,
        object? state,
        string rawPayload)
    {
        try
        {
            var message = new
            {
                type = "superisland_update",
                deviceId,
                deviceName,
                sourceId,
                isEnd,
                state,
                payload = rawPayload
            };

            var json = System.Text.Json.JsonSerializer.Serialize(message);
            return Task.FromResult(SendToAll(json));
        }
        catch (Exception ex)
        {
            logger?.LogError(ex, "本地Socket中继服务器: 发送超级岛消息失败");
            return Task.FromResult(false);
        }
    }

    /// <summary>
    /// Manages connected sessions and dispatches received data to the CommandReceived event.
    /// </summary>
    private sealed class RelayProvider : ITcpServerProvider
    {
        private readonly ConcurrentDictionary<TcpSession, byte> _sessions = new();

        public IEnumerable<TcpSession> Sessions => _sessions.Keys;

        public void OnConnected(ServerSession session)
        {
            _sessions.TryAdd(session, 0);
            logger?.LogInformation("本地Socket中继服务器: 客户端已连接，当前客户端数量: {ClientCount}", _sessions.Count);
        }

        public void OnDisconnected(ServerSession session)
        {
            _sessions.TryRemove(session, out _);
            logger?.LogInformation("本地Socket中继服务器: 客户端已断开，当前客户端数量: {ClientCount}", _sessions.Count);
        }

        public void OnError(SocketError error)
        {
            logger?.LogError("本地Socket中继服务器: 发生错误: {Error}", error);
        }

        public void OnReceived(ServerSession session, byte[] buffer, long offset, long size)
        {
            try
            {
                string receivedData = Encoding.UTF8.GetString(buffer, (int)offset, (int)size);
                // 可能包含多条指令，按换行符分割
                var commands = receivedData.Split('\n', StringSplitOptions.RemoveEmptyEntries);
                foreach (var cmd in commands)
                {
                    if (!string.IsNullOrWhiteSpace(cmd))
                    {
                        logger?.LogDebug("本地Socket中继服务器: 收到客户端指令: {Command}", cmd);
                        CommandReceived?.Invoke(null, cmd);
                    }
                }
            }
            catch (Exception ex)
            {
                logger?.LogError(ex, "本地Socket中继服务器: 处理接收数据时出错");
            }
        }
    }
}
