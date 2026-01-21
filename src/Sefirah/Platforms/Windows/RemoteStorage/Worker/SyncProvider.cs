using NotifyRelay.Platforms.Windows.Async;
using NotifyRelay.Platforms.Windows.Helpers;
using NotifyRelay.Platforms.Windows.RemoteStorage.Abstractions;
using NotifyRelay.Platforms.Windows.RemoteStorage.Commands;
using NotifyRelay.Platforms.Windows.RemoteStorage.Worker.IO;
using static Vanara.PInvoke.CldApi;

namespace NotifyRelay.Platforms.Windows.RemoteStorage.Worker;
public class SyncProvider(
    ISyncProviderContextAccessor contextAccessor,
    TaskQueue taskQueue,
    ShellCommandQueue shellCommandQueue,
    SyncRootConnector syncProvider,
    PlaceholdersService placeholdersService,
    ClientWatcher clientWatcher,
    RemoteWatcher remoteWatcher,
    ILogger logger
)
{
    public async Task Run(CancellationToken cancellation)
    {
        taskQueue.Start(cancellation);
        shellCommandQueue.Start(cancellation);

        // Hook up callback methods (in this class) for transferring files between client and server
        CF_CONNECTION_KEY connectionKey = default;
        bool connected = false;
        
        try
        {
            connectionKey = syncProvider.Connect();
            connected = true;
            
            // Create the placeholders in the client folder so the user sees something
            if (contextAccessor.Context.PopulationPolicy == PopulationPolicy.AlwaysFull)
            {
                placeholdersService.CreateBulk(string.Empty);
            }

            syncProvider.UpdatePlaceholders(contextAccessor.Context.RootDirectory);

            // Stage 2: Running
            //--------------------------------------------------------------------------------------------
            // The file watcher loop for this sample will run until the user presses Ctrl-C.
            // The file watcher will look for any changes on the files in the client (syncroot) in order
            // to let the cloud know.
            clientWatcher.Start();
            remoteWatcher.Start(cancellation);

            // Run until SIGTERM
            await cancellation;

            // 等待队列中的任务完成
            await shellCommandQueue.Stop();
            await taskQueue.Stop();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "同步提供程序运行失败");
        }
        finally
        {
            // 清理资源
            clientWatcher.Dispose();
            
            // 只有在连接成功时才断开连接
            if (connected)
            {
                logger.LogDebug("正在断开同步提供程序：{connectionKey}", connectionKey);
                syncProvider.Disconnect(connectionKey);
            }
        }

        logger.LogDebug("断开连接...");
    }
}
