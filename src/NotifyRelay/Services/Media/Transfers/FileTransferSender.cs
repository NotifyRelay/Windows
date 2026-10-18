using NotifyRelay.Data.Contracts;
using NotifyRelay.Data.Enums;
using NotifyRelay.Data.Models;
using NotifyRelay.Services.Protocol;
using NotifyRelay.Utils.Serialization;

namespace NotifyRelay.Services.Media.Transfers;

/// <summary>
/// 文件发送路径：单文件/批量文件的元数据下发、分块写流与进度通知。
/// </summary>
/// <remarks>
/// 承接原 <c>FileTransferService</c> 中的 <c>SendFile</c> / <c>SendBulkFiles</c> /
/// <c>SendFileData</c> 三个方法，方法体逐字搬移（仅把主类字段访问改为 <see cref="FileTransferState"/> 访问）。
///
/// 全部可变状态经构造注入的 <see cref="FileTransferState"/> 共享，因此
/// <c>ConnectionSource</c> / <c>TransferCompletionSource</c> 与主类的
/// 服务端事件回调仍指向同一实例，同步语义不变。
/// </remarks>
internal sealed class FileTransferSender(
    FileTransferState state,
    FileTransferServer server,
    Func<Task<ServerInfo>> initializeServer,
    ISessionManager sessionManager,
    IPlatformNotificationHandler notificationHandler,
    ILogger logger)
{
    public async Task SendFile(StorageFile file, FileMetadata metadata, PairedDevice device, FileTransferType transferType = FileTransferType.File)
    {
        try
        {
            if (!device.ConnectionStatus)
            {
                logger.LogWarning("设备未连接，无法发送文件");
                return;
            }
            // Wait for any existing transfer to complete
            if (state.TransferCompletionSource?.Task.IsCompleted == false)
            {
                await state.TransferCompletionSource.Task;
            }

            state.Server?.Stop();
            state.Session?.Disconnect();
            state.Session = null;
            state.ConnectionSource = null;

            state.CurrentTransfer = new TransferContext(
                device.Name,
                $"transfer_{DateTime.Now.Ticks}",
                [metadata]
            );

            state.CancellationTokenSource = new CancellationTokenSource();

            var serverInfo = await initializeServer();
            var transfer = new FileTransfer
            {
                TransferType = transferType,
                ServerInfo = serverInfo,
                FileMetadata = metadata
            };

            var json = SocketMessageSerializer.Serialize(transfer);
            logger.LogDebug($"发送元数据：{json}");
            sessionManager.SendMessage(device.Id, json);

            notificationHandler.ShowFileTransferNotification(
                string.Format("FileTransferNotification.Sending".GetLocalizedResource(), state.CurrentTransfer.Device),
                metadata.FileName,
                state.CurrentTransfer.TransferId,
                state.NotificationSequence++,
                0);

            state.TransferCompletionSource = new();
            await SendFileData(metadata, await file.OpenStreamForReadAsync());
            await state.TransferCompletionSource.Task;

            notificationHandler.ShowCompletedFileTransferNotification(
                string.Format("FileTransferNotification.SentSingle".GetLocalizedResource(), metadata.FileName, state.CurrentTransfer.Device),
                state.CurrentTransfer.TransferId);
        }
        catch (OperationCanceledException)
        {
            logger.LogInformation("文件传输被用户取消");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "发送流数据时出错");
        }
        finally
        {
            server.CleanupServer();
        }
    }

    public async Task SendBulkFiles(StorageFile[] files, PairedDevice device)
    {
        try
        {
            if (!device.ConnectionStatus)
            {
                logger.LogWarning("设备未连接，无法发送文件");
                return;
            }
            var fileMetadataList = await Task.WhenAll(files.Select(file => file.ToFileMetadata()));

            state.CurrentTransfer = new TransferContext(
                device.Name,
                $"transfer_{DateTime.Now.Ticks}",
                fileMetadataList.ToList()
            );

            state.CancellationTokenSource = new CancellationTokenSource();

            state.ServerInfo = await initializeServer();

            var transfer = new BulkFileTransfer
            {
                ServerInfo = state.ServerInfo,
                Files = [.. fileMetadataList]
            };

            // Send metadata first
            sessionManager.SendMessage(device.Id, SocketMessageSerializer.Serialize(transfer));

            notificationHandler.ShowFileTransferNotification(
                string.Format("FileTransferNotification.SendingBulk".GetLocalizedResource(), 1, state.CurrentTransfer.Files.Count, state.CurrentTransfer.Device),
                state.CurrentTransfer.Files[0].FileName,
                state.CurrentTransfer.TransferId,
                state.NotificationSequence++);

            for (int i = 0; i < files.Length; i++)
            {
                try
                {
                    state.CancellationTokenSource.Token.ThrowIfCancellationRequested();

                    logger.LogDebug($"正在发送文件：{fileMetadataList[i].FileName}");

                    state.TransferCompletionSource = new TaskCompletionSource<bool>();

                    await SendFileData(fileMetadataList[i], await files[i].OpenStreamForReadAsync());

                    await state.TransferCompletionSource.Task;
                    state.CurrentTransfer.CurrentFileIndex++;
                }
                catch (OperationCanceledException)
                {
                    logger.LogInformation("批量文件传输被用户取消");
                    throw;
                }
            }

            notificationHandler.ShowCompletedFileTransferNotification(
                string.Format("FileTransferNotification.SentBulk".GetLocalizedResource(), state.CurrentTransfer.Files.Count, state.CurrentTransfer.Device),
                state.CurrentTransfer.TransferId);

            logger.LogDebug("所有文件已成功传输");
        }
        catch (OperationCanceledException)
        {
            logger.LogInformation("批量文件传输被用户取消");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "SendBulkFiles 内部出错");
        }
        finally
        {
            server.CleanupServer();
        }
    }

    public async Task SendFileData(FileMetadata metadata, Stream stream)
    {
        try
        {
            if (state.Session == null)
            {
                state.ConnectionSource = new();

                // Wait for Authentication from onReceived event to trigger
                state.Session = await state.ConnectionSource.Task;
            }

            const int ChunkSize = 524288; // 512KB

            using (stream)
            {
                var buffer = new byte[ChunkSize];
                long totalBytesRead = 0;

                while (totalBytesRead < metadata.FileSize && state.Session?.IsConnected == true)
                {
                    // Check if transfer has been canceled
                    state.CancellationTokenSource?.Token.ThrowIfCancellationRequested();

                    int bytesRead = await stream.ReadAsync(buffer);
                    if (bytesRead == 0) break;

                    state.Session.Send(buffer, 0, bytesRead);
                    totalBytesRead += bytesRead;

                    if (state.CurrentTransfer != null)
                    {
                        state.CurrentTransfer.BytesTransferred = totalBytesRead;
                        var progress = (double)totalBytesRead / metadata.FileSize * 100;

                        if (state.CurrentTransfer.Files.Count > 1)
                        {
                            notificationHandler.ShowFileTransferNotification(
                                string.Format("FileTransferNotification.SendingBulk".GetLocalizedResource(), state.CurrentTransfer.CurrentFileIndex + 1, state.CurrentTransfer.Files.Count, state.CurrentTransfer.Device),
                                metadata.FileName,
                                state.CurrentTransfer.TransferId,
                                state.NotificationSequence,
                                progress);
                        }
                        else
                        {
                            notificationHandler.ShowFileTransferNotification(
                                string.Format("FileTransferNotification.Sending".GetLocalizedResource(), state.CurrentTransfer.Device),
                                metadata.FileName,
                                state.CurrentTransfer.TransferId,
                                state.NotificationSequence,
                                progress);
                        }
                    }
                }
            }

            logger.LogInformation($"已完成文件传输：{metadata.FileName}");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "SendFileData 内部出错");
            throw;
        }
    }
}

