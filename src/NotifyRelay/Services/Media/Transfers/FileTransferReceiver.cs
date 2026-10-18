using System.Text;
using NotifyRelay.Data.Contracts;
using NotifyRelay.Data.Models;
using NotifyRelay.Services.Protocol;

namespace NotifyRelay.Services.Media.Transfers;

/// <summary>
/// 文件接收路径：批量/单文件落盘、客户端清理。
/// </summary>
/// <remarks>
/// 承接原 <c>FileTransferService</c> 中的 <c>ReceiveBulkFiles</c> / <c>ReceiveFile</c> /
/// <c>CleanupFileStream</c> / <c>CleanupClient</c> 四个方法，方法体逐字搬移
/// （仅把主类字段访问改为 <see cref="FileTransferState"/> 访问）。
///
/// <c>FileReceived</c> 事件由主类声明（接口契约），经构造注入的回调
/// <paramref name="onFileReceived"/> 触发，保持事件语义与订阅者不变。
/// </remarks>
internal sealed class FileTransferReceiver(
    FileTransferState state,
    IUserSettingsService userSettingsService,
    IPlatformNotificationHandler notificationHandler,
    ITcpClientProvider clientProvider,
    Action<PairedDevice, StorageFile> onFileReceived,
    ILogger logger)
{
    public async Task ReceiveBulkFiles(BulkFileTransfer bulkFile, PairedDevice device)
    {
        try
        {
            if (state.TransferCompletionSource?.Task is not null)
            {
                await state.TransferCompletionSource.Task;
            }

            state.StorageLocation = userSettingsService.GeneralSettingsService.ReceivedFilesPath;
            var serverInfo = bulkFile.ServerInfo;

            state.CurrentTransfer = new TransferContext(
                device.Name,
                $"transfer_{DateTime.Now.Ticks}",
                bulkFile.Files
            );

            state.CancellationTokenSource = new CancellationTokenSource();

            state.Client = new Client(serverInfo.IpAddress, serverInfo.Port, clientProvider);

            if (!state.Client.ConnectAsync())
                throw new IOException("Failed to connect to file transfer server");

            // Adding a small delay for the android to open a read channel
            await Task.Delay(500);
            var passwordBytes = Encoding.UTF8.GetBytes(serverInfo.Password + "\n");
            state.Client?.SendAsync(passwordBytes);

            notificationHandler.ShowFileTransferNotification(
                string.Format("FileTransferNotification.ReceivingBulk".GetLocalizedResource(), 1, state.CurrentTransfer.Files.Count, state.CurrentTransfer.Device),
                state.CurrentTransfer.Files[0].FileName,
                state.CurrentTransfer.TransferId,
                state.NotificationSequence++);

            foreach (var fileMetadata in bulkFile.Files)
            {
                try
                {
                    // Check if the entire bulk transfer has been cancelled
                    state.CancellationTokenSource.Token.ThrowIfCancellationRequested();

                    logger.LogInformation($"开始接收文件 {state.CurrentTransfer.CurrentFileIndex + 1}/{bulkFile.Files.Count}：{fileMetadata.FileName}");

                    if (state.TransferCompletionSource?.Task is not null && !state.TransferCompletionSource.Task.IsCompleted)
                    {
                        await state.TransferCompletionSource.Task;
                    }
                    string fullPath = Path.Combine(state.StorageLocation, fileMetadata.FileName);
                    Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);

                    state.TransferCompletionSource = new();
                    state.CurrentFileMetadata = fileMetadata;
                    state.CurrentFileStream = new FileStream(fullPath, FileMode.Create);

                    // Wait for this file transfer to complete
                    await state.TransferCompletionSource.Task;

                    state.CurrentTransfer.CurrentFileIndex++;
                    logger.LogInformation($"已接收文件 {fileMetadata.FileName}");

                    // Clean up the file stream for the previous file
                    CleanupFileStream();
                }
                catch (Exception ex)
                {
                    CleanupFileStream();
                    var failedFilePath = Path.Combine(state.StorageLocation, fileMetadata.FileName);
                    if (File.Exists(failedFilePath))
                    {
                        File.Delete(failedFilePath);
                    }

                    if (ex is OperationCanceledException)
                    {
                        logger.LogInformation("批量文件传输被用户取消");
                        throw;
                    }
                    logger.LogError(ex, $"接收文件 {fileMetadata.FileName} 时出错");
                }
            }

            notificationHandler.ShowCompletedFileTransferNotification(
                string.Format("FileTransferNotification.CompletedBulk".GetLocalizedResource(), state.CurrentTransfer.Files.Count, state.CurrentTransfer.Device),
                state.CurrentTransfer.TransferId,
                folderPath: state.StorageLocation);

            logger.LogWarning($"批量文件传输完成，但存在错误：{state.CurrentTransfer.CurrentFileIndex}/{state.CurrentTransfer.Files.Count} 个文件接收失败");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "批量文件传输设置期间出错");
        }
        finally
        {
            CleanupClient();
        }
    }

    public void CleanupFileStream()
    {
        state.CurrentFileStream?.Close();
        state.CurrentFileStream?.Dispose();
        state.CurrentFileStream = null;
        state.CurrentFileMetadata = null;
    }

    public async Task ReceiveFile(FileTransfer data, PairedDevice device)
    {
        state.StorageLocation = userSettingsService.GeneralSettingsService.ReceivedFilesPath;
        string fullPath = Path.Combine(state.StorageLocation, data.FileMetadata.FileName);
        try
        {
            // Wait for any existing transfer to complete
            if (state.TransferCompletionSource?.Task is not null)
            {
                await state.TransferCompletionSource.Task;
            }

            // Create transfer context for single file
            state.CurrentTransfer = new TransferContext(
                device.Name,
                $"transfer_{DateTime.Now.Ticks}",
                [data.FileMetadata]
            );

            state.TransferCompletionSource = new TaskCompletionSource<bool>();
            state.CancellationTokenSource = new CancellationTokenSource();
            var serverInfo = data.ServerInfo;
            state.CurrentFileMetadata = data.FileMetadata;

            state.CurrentFileStream = new FileStream(fullPath, FileMode.Create);

            state.Client = new Client(serverInfo.IpAddress, serverInfo.Port, clientProvider);
            if (!state.Client.ConnectAsync())
                throw new IOException("Failed to connect to file transfer server");

            notificationHandler.ShowFileTransferNotification(
                string.Format("FileTransferNotification.Receiving".GetLocalizedResource(), state.CurrentTransfer.Device),
                state.CurrentFileMetadata.FileName,
                state.CurrentTransfer.TransferId,
                state.NotificationSequence++,
                0);

            // Adding a small delay for the android to open a read channel
            await Task.Delay(500);
            var passwordBytes = Encoding.UTF8.GetBytes(serverInfo.Password + "\n");
            state.Client?.SendAsync(passwordBytes);

            await state.TransferCompletionSource.Task;

            if (device.DeviceSettings.ClipboardFilesEnabled)
            {
                var file = await StorageFile.GetFileFromPathAsync(fullPath);
                onFileReceived(device, file);
            }

            notificationHandler.ShowCompletedFileTransferNotification(
                string.Format("FileTransferNotification.CompletedSingle".GetLocalizedResource(), state.CurrentFileMetadata.FileName, state.CurrentTransfer.Device),
                state.CurrentTransfer.TransferId,
                fullPath);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "文件传输设置期间出错");
            CleanupFileStream();
            if (File.Exists(fullPath))
            {
                File.Delete(fullPath);
            }
        }
        finally
        {
            CleanupClient();
        }
    }

    public void CleanupClient()
    {
        try
        {
            CleanupFileStream();

            try
            {
                state.Client?.DisconnectAsync();
            }
            catch
            {
                // Ignore disconnect errors during cleanup
            }
            state.Client?.Dispose();
            state.Client = null;
            state.TransferCompletionSource = null;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "传输清理期间出错");
        }
        finally
        {
            state.CurrentFileMetadata = null;
            state.CurrentTransfer = null;
            state.CancellationTokenSource?.Dispose();
            state.CancellationTokenSource = null;
        }
    }
}

