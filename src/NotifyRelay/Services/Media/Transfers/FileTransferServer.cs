using System.Net;
using NotifyRelay.Data.Models;
using NotifyRelay.Services.Protocol;

namespace NotifyRelay.Services.Media.Transfers;

/// <summary>
/// 文件传输服务端生命周期（端口探测/启动、停止/清理）。
/// </summary>
/// <remarks>
/// 仅承接原 <c>FileTransferService</c> 中「服务端的创建与销毁」这一职责。
///
/// **不使用本类型作为 <see cref="ITcpServerProvider"/>**：原实现中
/// <c>OnConnected</c>/<c>OnDisconnected</c>/<c>OnError</c>/<c>OnReceived</c> 由
/// <c>FileTransferService</c> 自身实现，且 <c>OnError(SocketError)</c> 是**同时满足**
/// <see cref="ITcpServerProvider"/> 与 <c>ITcpClientProvider</c> 的同一个方法
/// （方法体执行的是客户端清理）。若改由本类型充当 provider，会静默改变
/// <c>OnError</c> 的走向。因此 <see cref="InitializeServer"/> 通过参数接收 provider，
/// 由主类传入自身，回调路由与拆分前完全一致。
/// </remarks>
internal sealed class FileTransferServer(
    FileTransferState state,
    ILogger logger)
{
    /// <summary>发送元数据的完成标记（协议约定，值不得修改）。</summary>
    internal const string COMPLETE_MESSAGE = "Complete";

    /// <summary>发送路径可用于监听的端口区间（协议约定，不得修改）。</summary>
    private readonly IEnumerable<int> PORT_RANGE = Enumerable.Range(5152, 18);

    /// <summary>
    /// 在端口区间内探测并启动 TCP 服务端。
    /// </summary>
    /// <param name="provider">服务端事件接收者（由主类传入自身，保持回调路由不变）。</param>
    public Task<ServerInfo> InitializeServer(ITcpServerProvider provider)
    {
        // Try each port in the range
        foreach (int port in PORT_RANGE)
        {
            try
            {
                state.Server = new Server(IPAddress.Any, port, provider, logger)
                {
                    OptionDualMode = true,
                    OptionReuseAddress = true,
                };
                state.Server.Start();

                state.ServerInfo = new ServerInfo
                {
                    Port = port,
                    Password = NotifyCryptoHelper.GenerateRandomPassword()
                };

                logger.LogInformation($"文件传输服务器已在 {state.ServerInfo.IpAddress}:{state.ServerInfo.Port} 初始化");
                return Task.FromResult(state.ServerInfo);
            }
            catch (Exception ex)
            {
                logger.LogDebug($"启动端口 {port} 的服务器失败：{ex.Message}");

                state.Server?.Dispose();
                state.Server = null;
            }
        }
        throw new IOException("启动文件传输服务器失败：范围内端口均不可用");
    }

    /// <summary>
    /// 停止并释放服务端，清空发送路径状态。
    /// </summary>
    public void CleanupServer()
    {
        state.Server?.Stop();
        state.Server?.Dispose();
        state.Server = null;
        state.ServerInfo = null;
        state.ConnectionSource = null;
        state.Session = null;
        state.CurrentTransfer = null;
        state.CancellationTokenSource?.Dispose();
        state.CancellationTokenSource = null;
    }
}

