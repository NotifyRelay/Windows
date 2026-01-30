using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace NotifyRelay.Services;

/// <summary>
/// Simple local TCP server for delivering notifications to UWP widget clients.
/// Messages are sent as UTF-8 JSON lines delimited by '\n'.
/// </summary>
public static class LocalSocketRelayServer
{
    private static TcpListener? listener;
    private static readonly ConcurrentBag<TcpClient> clients = new();
    private static CancellationTokenSource? cts;
    private static ILogger? logger;

    // 事件定义：当收到客户端指令时触发
    public static event EventHandler<string>? CommandReceived;

    public static bool IsRunning => listener != null;

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
            if (listener != null) return;
            cts = new CancellationTokenSource();
            listener = new TcpListener(IPAddress.Loopback, port);
            listener.Start();
            _ = Task.Run(() => AcceptLoopAsync(listener, cts.Token));
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
            cts?.Cancel();
            listener?.Stop();
            listener = null;
            foreach (var c in clients)
            {
                try { c.Close(); } catch { }
            }
            while (!clients.IsEmpty) clients.TryTake(out _);
            logger?.LogInformation("本地Socket中继服务器: 已停止");
        }
        catch (Exception ex)
        {
            logger?.LogError(ex, "本地Socket中继服务器: 停止错误");
        }
    }

    private static async Task AcceptLoopAsync(TcpListener l, CancellationToken token)
    {
        try
        {
            while (!token.IsCancellationRequested)
            {
                var client = await l.AcceptTcpClientAsync();
                // 检查客户端是否真的连接
                if (client.Connected)
                {
                    logger?.LogInformation("本地Socket中继服务器: 客户端已连接，添加到客户端列表");
                    clients.Add(client);
                    logger?.LogInformation("本地Socket中继服务器: 当前客户端数量: {ClientCount}", clients.Count);
                    _ = Task.Run(() => ClientLoopAsync(client, token));
                }
                else
                {
                    logger?.LogWarning("本地Socket中继服务器: 客户端连接已断开，未添加到客户端列表");
                    client.Close();
                }
            }
        }
        catch (ObjectDisposedException) { }
        catch (Exception ex)
        {
            logger?.LogError(ex, "本地Socket中继服务器: 接受连接循环错误");
        }
    }

    private static async Task ClientLoopAsync(TcpClient client, CancellationToken token)
    {
        try
        {
            using (client)
            using (var stream = client.GetStream())
            {
                var buffer = new byte[1024];
                while (!token.IsCancellationRequested && client.Connected)
                {
                    // 定期检查客户端连接状态
                    if (!client.Connected)
                    {
                        logger?.LogWarning("本地Socket中继服务器: 客户端已断开连接");
                        break;
                    }
                    // 定期检查是否有数据可读，避免连接被防火墙关闭
                    if (stream.DataAvailable)
                    {
                        // 读取数据
                        var bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length, token);
                        if (bytesRead == 0)
                        {
                            logger?.LogWarning("本地Socket中继服务器: 客户端已关闭连接");
                            break;
                        }

                        // 处理接收到的数据
                        try
                        {
                            string receivedData = Encoding.UTF8.GetString(buffer, 0, bytesRead);
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
                    // keep the connection open; we only push from server side
                    await Task.Delay(500, token); // 缩短检查间隔以提高响应速度
                }
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            logger?.LogInformation(ex, "本地Socket中继服务器: 客户端循环意外结束");
        }
        finally
        {
            // 从客户端列表中移除断开连接的客户端
            try
            {
                // 由于ConcurrentBag无法直接移除特定元素，我们需要重新创建一个新的集合
                // 这是一个更可靠的清理方式
                var newClients = new List<TcpClient>();
                TcpClient currentClient;
                while (clients.TryTake(out currentClient))
                {
                    if (currentClient.Connected)
                    {
                        newClients.Add(currentClient);
                    }
                }
                // 将仍然连接的客户端放回集合
                foreach (var c in newClients)
                {
                    clients.Add(c);
                }
                logger?.LogInformation("本地Socket中继服务器: 客户端清理完成，当前客户端数量: {ClientCount}", clients.Count);
            }
            catch (Exception ex)
            {
                logger?.LogError(ex, "本地Socket中继服务器: 从客户端列表中移除客户端时出错");
            }
        }
    }

    public static async Task<bool> SendNotificationAsync(string appName, string packageName, string title, string body, string? iconUrl = null)
    {
        try
        {
            // 创建通知对象
            var notification = new { appName, packageName, title, body, iconUrl };
            // 序列化JSON，确保正确处理特殊字符
            var json = System.Text.Json.JsonSerializer.Serialize(notification);
            var payload = json + "\n";
            logger?.LogInformation("本地Socket中继服务器: 尝试向 {ClientCount} 个客户端发送通知", clients.Count);

            var data = Encoding.UTF8.GetBytes(payload);
            bool sentToAnyClient = false;
            int sentCount = 0;
            int failedCount = 0;

            // 创建一个临时列表，避免在迭代过程中修改原集合
            var clientList = clients.ToList();
            logger?.LogInformation("本地Socket中继服务器: 实际处理 {ClientListCount} 个客户端", clientList.Count);

            foreach (var client in clientList)
            {
                try
                {
                    if (!client.Connected)
                    {
                        logger?.LogWarning("本地Socket中继服务器: 客户端已断开连接，跳过发送");
                        failedCount++;
                        continue;
                    }
                    var stream = client.GetStream();
                    await stream.WriteAsync(data, 0, data.Length);
                    await stream.FlushAsync();
                    sentToAnyClient = true;
                    sentCount++;
                    logger?.LogDebug("本地Socket中继服务器: 成功发送通知到客户端");
                }
                catch (Exception ex)
                {
                    logger?.LogInformation(ex, "本地Socket中继服务器: 向客户端发送失败");
                    failedCount++;
                    try { client.Close(); } catch { }
                }
            }

            logger?.LogInformation("本地Socket中继服务器: 通知发送结果 - 总数: {ClientListCount}, 成功: {SentCount}, 失败: {FailedCount}",
                clientList.Count, sentCount, failedCount);

            // 清理断开连接的客户端
            CleanupDisconnectedClients();

            return sentToAnyClient;
        }
        catch (System.Text.Json.JsonException ex)
        {
            logger?.LogError(ex, "本地Socket中继服务器: JSON序列化失败");
            return false;
        }
        catch (Exception ex)
        {
            logger?.LogError(ex, "本地Socket中继服务器: 发送通知时发生意外错误");
            return false;
        }
    }

    public static async Task<bool> SendMediaInfoAsync(string deviceId, string title, string artist, string coverUrl, bool isPlaying)
    {
        try
        {
            var mediaInfo = new
            {
                type = "media_update",
                deviceId,
                title,
                artist,
                coverUrl,
                isPlaying
            };

            var json = System.Text.Json.JsonSerializer.Serialize(mediaInfo);
            var payload = json + "\n";
            var data = Encoding.UTF8.GetBytes(payload);

            bool sentToAnyClient = false;
            var clientList = clients.ToList();

            foreach (var client in clientList)
            {
                try
                {
                    if (client.Connected)
                    {
                        var stream = client.GetStream();
                        await stream.WriteAsync(data, 0, data.Length);
                        await stream.FlushAsync();
                        sentToAnyClient = true;
                    }
                }
                catch { }
            }

            return sentToAnyClient;
        }
        catch (Exception ex)
        {
            logger?.LogError(ex, "本地Socket中继服务器: 发送媒体信息失败");
            return false;
        }
    }

    /// <summary>
    /// 清理断开连接的客户端
    /// </summary>
    private static void CleanupDisconnectedClients()
    {
        try
        {
            var newClients = new List<TcpClient>();
            TcpClient c;
            while (clients.TryTake(out c))
            {
                if (c.Connected)
                {
                    newClients.Add(c);
                }
                else
                {
                    try { c.Close(); } catch { }
                }
            }
            // 将仍然连接的客户端放回集合
            foreach (var client in newClients)
            {
                clients.Add(client);
            }
            logger?.LogInformation("本地Socket中继服务器: 清理完成，当前客户端数量: {ClientCount}", clients.Count);
        }
        catch (Exception ex)
        {
            logger?.LogError(ex, "本地Socket中继服务器: 清理客户端时出错");
        }
    }
}