using NotifyRelay.Data.Models;
using NotifyRelay.Services.Protocol;

namespace NotifyRelay.Services.Media.Transfers;

/// <summary>
/// 文件传输共享状态。
/// </summary>
/// <remarks>
/// 拆分前这些字段全部散落在 <c>FileTransferService</c> 中，且被「发送路径」「接收路径」
/// 「服务端事件回调」「客户端事件回调」四方共同读写。把它们集中到本类型，是为了在**不改变任何
/// 同步语义**的前提下，让拆分出的协作者与原主类访问到同一份状态：
///
/// - <see cref="ConnectionSource"/> / <see cref="TransferCompletionSource"/> 各自**全类只有一份实例**。
///   `TrySetResult` 的调用点（服务端事件回调）与 `await` 点（发送路径）指向的就是同一对象。
/// - 字段名、初始值、可空性与拆分前逐字一致（含 <see cref="NotificationSequence"/> = 1、
///   <see cref="BytesTransferred"/> = 0）。
///
/// **本类型不持有任何服务依赖，也不含任何业务逻辑**，仅承载可变状态。
/// </remarks>
internal sealed class FileTransferState
{
    /// <summary>收到文件的落盘根目录（接收路径每次传输前刷新）。</summary>
    public string? StorageLocation { get; set; }

    /// <summary>接收路径当前正在写入的文件流。</summary>
    public FileStream? CurrentFileStream { get; set; }

    /// <summary>接收路径当前文件对应的元数据。</summary>
    public FileMetadata? CurrentFileMetadata { get; set; }

    /// <summary>接收路径当前文件已累计写入的字节数（每个文件完成后归零）。</summary>
    public long BytesTransferred { get; set; }

    /// <summary>接收路径使用的 TCP 客户端（发送路径不使用）。</summary>
    public Client? Client { get; set; }

    /// <summary>发送路径监听的 TCP 服务器。</summary>
    public Server? Server { get; set; }

    /// <summary>发送路径监听的服务器信息（端口 + 一次性密码）。</summary>
    public ServerInfo? ServerInfo { get; set; }

    /// <summary>发送路径当前已建立的会话（认证通过后由服务端事件写入）。</summary>
    public ServerSession? Session { get; set; }

    /// <summary>通知去重用的自增序号（语义与拆分前一致）。</summary>
    public uint NotificationSequence { get; set; } = 1;

    /// <summary>当前传输的上下文（设备名、传输 ID、文件列表、进度）。</summary>
    public TransferContext? CurrentTransfer { get; set; }

    /// <summary>取消令牌源（发送路径按文件重建，接收路径每次传输重建）。</summary>
    public CancellationTokenSource? CancellationTokenSource { get; set; }

    /// <summary>连接建立信号：服务端事件回调 TrySetResult，发送路径 await。</summary>
    public TaskCompletionSource<ServerSession>? ConnectionSource { get; set; }

    /// <summary>传输完成信号：两端事件回调 TrySetResult，发送/接收路径 await。</summary>
    public TaskCompletionSource<bool>? TransferCompletionSource { get; set; }
}

