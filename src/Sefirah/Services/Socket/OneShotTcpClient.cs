using System.Net.Sockets;
using System.Text;

namespace NotifyRelay.Services.Socket;

/// <summary>
/// 一次性 TCP 客户端工具类
///
/// 封装「创建连接 → 发送报文 → 读取响应 → 关闭」的模板逻辑，
/// 消除 ProtocolSender 中的重复 Socket 代码。
/// </summary>
public static class OneShotTcpClient
{
    private const int DefaultConnectTimeout = 5000;
    private const int DefaultTimeout = 80000;

    /// <summary>
    /// 发送报文并返回完整响应。
    /// </summary>
    public static async Task<string?> SendAndReceiveAsync(
        string ip,
        int port,
        string payload,
        int connectTimeoutMs = DefaultConnectTimeout,
        int timeoutMs = DefaultTimeout)
    {
        using var tcpClient = new TcpClient();
        tcpClient.ReceiveTimeout = timeoutMs;
        tcpClient.SendTimeout = timeoutMs;

        try
        {
            var connectTask = tcpClient.ConnectAsync(ip, port);
            var delayTask = Task.Delay(connectTimeoutMs);
            var connectResult = await Task.WhenAny(connectTask, delayTask);

            if (connectResult == delayTask || !connectTask.IsCompletedSuccessfully)
            {
                return null;
            }

            using var stream = tcpClient.GetStream();
            stream.ReadTimeout = timeoutMs;
            stream.WriteTimeout = timeoutMs;

            var messageBytes = Encoding.UTF8.GetBytes(payload + "\n");
            await stream.WriteAsync(messageBytes);
            await stream.FlushAsync();

            using var reader = new StreamReader(stream, Encoding.UTF8);
            return await reader.ReadLineAsync();
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// 仅发送报文，不等待响应。
    /// </summary>
    public static async Task<bool> SendOnlyAsync(
        string ip,
        int port,
        string payload,
        int connectTimeoutMs = DefaultConnectTimeout,
        int timeoutMs = DefaultTimeout)
    {
        using var tcpClient = new TcpClient();
        tcpClient.SendTimeout = timeoutMs;

        try
        {
            var connectTask = tcpClient.ConnectAsync(ip, port);
            var delayTask = Task.Delay(connectTimeoutMs);
            var connectResult = await Task.WhenAny(connectTask, delayTask);

            if (connectResult == delayTask || !connectTask.IsCompletedSuccessfully)
            {
                return false;
            }

            using var stream = tcpClient.GetStream();
            stream.WriteTimeout = timeoutMs;
            var messageBytes = Encoding.UTF8.GetBytes(payload + "\n");
            await stream.WriteAsync(messageBytes);
            await stream.FlushAsync();
            return true;
        }
        catch
        {
            return false;
        }
    }
}
