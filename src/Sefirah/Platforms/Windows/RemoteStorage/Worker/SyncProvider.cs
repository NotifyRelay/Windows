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
        try
        {
            var connectionKey = syncProvider.Connect();
            // 只有在连接成功时才创建Disposable对象
            using var connectDisposable = new Disposable<CF_CONNECTION_KEY>(connectionKey, syncProvider.Disconnect);
            
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
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "同步提供程序初始化失败");
            // 初始化失败时，跳过后续步骤，等待取消信号
        }

        // Run until SIGTERM
        await cancellation;

        await shellCommandQueue.Stop();

        await taskQueue.Stop();

        logger.LogDebug("断开连接...");
    }
}
