using System.Net.Sockets;
using System.Text;
using NotifyRelay.Data.Contracts;
using NotifyRelay.Data.Enums;
using NotifyRelay.Data.Models;
using NotifyRelay.Dialogs;
using NotifyRelay.Services.Media.Transfers;
using NotifyRelay.Services.Protocol;

namespace NotifyRelay.Services.Media;

public class FileTransferService : IFileTransferService, ITcpClientProvider, ITcpServerProvider
{
    private readonly IDeviceManager deviceManager;
    private readonly ISessionManager sessionManager;
    private readonly IUserSettingsService userSettingsService;
    private readonly IPlatformNotificationHandler notificationHandler;
    private readonly ILogger logger;

    // 传输共享状态（拆分前为主类字段，现集中承载；仍全类只有一份，语义不变）
    private readonly FileTransferState state = new();

    // 协作者（不进 DI 容器，由本类组合）
    private readonly FileTransferServer server;
    private readonly FileTransferReceiver receiver;
    private readonly FileTransferSender sender;

    public event EventHandler<(PairedDevice device, StorageFile data)>? FileReceived;

    public FileTransferService(
        ILogger logger,
        ISessionManager sessionManager,
        IUserSettingsService userSettingsService,
        IDeviceManager deviceManager,
        IPlatformNotificationHandler notificationHandler)
    {
        this.logger = logger;
        this.sessionManager = sessionManager;
        this.userSettingsService = userSettingsService;
        this.deviceManager = deviceManager;
        this.notificationHandler = notificationHandler;

        server = new FileTransferServer(state, logger);
        receiver = new FileTransferReceiver(
            state, userSettingsService, notificationHandler, this, OnFileReceived, logger);
        // 服务端 provider 仍为主类自身，保证 OnError 等回调路由与拆分前一致
        sender = new FileTransferSender(
            state, server, () => server.InitializeServer(this), sessionManager, notificationHandler, logger);
    }

    private void OnFileReceived(PairedDevice device, StorageFile file)
        => FileReceived?.Invoke(this, (device, file));

    public void CancelTransfer()
    {
        state.CancellationTokenSource?.Cancel();
    }

    #region Receive
    public Task ReceiveBulkFiles(BulkFileTransfer bulkFile, PairedDevice device)
        => receiver.ReceiveBulkFiles(bulkFile, device);

    public Task ReceiveFile(FileTransfer data, PairedDevice device)
        => receiver.ReceiveFile(data, device);
    #endregion

    #region Send
    public async void SendFiles(IReadOnlyList<IStorageItem> storageItems)
    {
        try
        {
            var files = storageItems.OfType<StorageFile>().ToArray();
            var devices = deviceManager.PairedDevices.Where(d => d.ConnectionStatus).ToList();
            PairedDevice? selectedDevice = null;
            if (devices.Count == 0)
            {
                return;
            }
            else if (devices.Count == 1)
            {
                selectedDevice = devices[0];
            }
            else if (devices.Count > 1)
            {
                App.MainWindow.AppWindow.Show();
                App.MainWindow.Activate();
                selectedDevice = await DeviceSelector.ShowDeviceSelectionDialog(devices);
            }

            if (selectedDevice == null || selectedDevice.Session == null) return;

            await Task.Run(async () =>
            {
                if (files.Length > 1)
                {
                    await sender.SendBulkFiles(files, selectedDevice);
                }
                else if (files.Length == 1)
                {
                    var metadata = await files[0].ToFileMetadata();
                    if (metadata == null) return;

                    await sender.SendFile(files[0], metadata, selectedDevice);
                }
            });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, $"发送文件时出错：{ex.Message}");
        }
    }

    public Task SendFile(StorageFile file, FileMetadata metadata, PairedDevice device, FileTransferType transferType = FileTransferType.File)
        => sender.SendFile(file, metadata, device, transferType);

    public Task SendBulkFiles(StorageFile[] files, PairedDevice device)
        => sender.SendBulkFiles(files, device);
    #endregion

    /// <summary>
    /// 在端口区间内探测并启动 TCP 服务端（保留原公开入口）。
    /// </summary>
    public Task<ServerInfo> InitializeServer() => server.InitializeServer(this);

    #region Client Events

    public void OnConnected()
    {
        logger.LogInformation("已连接到文件传输服务器");
    }

    public void OnDisconnected()
    {
        logger.LogInformation("已从文件传输服务器断开连接");
        if (state.CurrentFileMetadata != null &&
            state.CurrentFileStream != null &&
            state.CurrentTransfer != null &&
            state.CurrentTransfer.BytesTransferred < state.CurrentFileMetadata.FileSize)
        {
            state.TransferCompletionSource?.TrySetException(new IOException("Connection to server lost"));
            receiver.CleanupClient();
        }
    }

    public void OnError(SocketError error)
    {
        logger.LogError($"文件传输期间发生 Socket 错误：{error}");
        state.TransferCompletionSource?.TrySetException(new IOException($"Socket 错误：{error}"));
        receiver.CleanupClient();
    }

    public void OnReceived(byte[] buffer, long offset, long size)
    {
        try
        {
            state.CancellationTokenSource?.Token.ThrowIfCancellationRequested();

            if (state.CurrentFileStream == null || state.CurrentFileMetadata == null || state.CurrentTransfer == null) return;

            state.CurrentFileStream.Write(buffer, (int)offset, (int)size);
            state.BytesTransferred += size;
            state.CurrentTransfer.BytesTransferred += size;

            var progress = (double)state.CurrentTransfer.BytesTransferred / state.CurrentTransfer.TotalBytes * 100;

            notificationHandler.ShowFileTransferNotification(
                string.Format("FileTransferNotification.Receiving".GetLocalizedResource(), state.CurrentTransfer.Device),
                state.CurrentFileMetadata.FileName,
                state.CurrentTransfer.TransferId,
                state.NotificationSequence,
                progress);

            if (state.BytesTransferred >= state.CurrentFileMetadata.FileSize)
            {
                state.Client?.Send(Encoding.UTF8.GetBytes(FileTransferServer.COMPLETE_MESSAGE + "\n"));
                state.BytesTransferred = 0;
                state.TransferCompletionSource?.TrySetResult(true);
            }
        }
        catch (Exception ex)
        {
            state.TransferCompletionSource?.TrySetException(ex);
        }
    }
    #endregion

    #region Server Events
    public void OnConnected(ServerSession session)
    {
        logger.LogInformation($"客户端已连接到文件传输服务器：{session.Id}");
    }

    public void OnDisconnected(ServerSession session)
    {
        if (state.TransferCompletionSource?.Task.IsCompleted == false)
        {
            state.TransferCompletionSource?.TrySetException(new Exception("Client disconnected"));
        }
        logger.LogInformation($"客户端已从文件传输服务器断开连接：{session.Id}");
        server.CleanupServer();
    }

    public void OnReceived(ServerSession session, byte[] buffer, long offset, long size)
    {
        string message = Encoding.UTF8.GetString(buffer, (int)offset, (int)size);
        if (state.ConnectionSource?.Task.IsCompleted == false && message == state.ServerInfo?.Password)
        {
            state.ConnectionSource.SetResult(session);
        }
        if (message == FileTransferServer.COMPLETE_MESSAGE)
        {
            logger.LogInformation($"传输完成");
            state.TransferCompletionSource?.TrySetResult(true);
        }
    }
    #endregion
}
