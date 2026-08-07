using System.Runtime.InteropServices.WindowsRuntime;
using NotifyRelay.Platforms.Windows.RemoteStorage.Abstractions;
using NotifyRelay.Platforms.Windows.RemoteStorage.Commands;
using NotifyRelay.Platforms.Windows.RemoteStorage.RemoteAbstractions;
using Vanara.PInvoke;
using Windows.Storage.Provider;

namespace NotifyRelay.Platforms.Windows.RemoteStorage.Worker;

public class SyncProviderPool(
    IServiceScopeFactory scopeFactory,
    ILogger logger)
{
    private readonly Dictionary<string, CancellableThread> _threads = [];
    private readonly object _lock = new();
    private bool _stopping = false;

    public void Start(StorageProviderSyncRootInfo syncRootInfo)
    {
        if (_stopping)
        {
            return;
        }

        lock (_lock)
        {
            // If there's an existing thread, stop it first
            if (_threads.TryGetValue(syncRootInfo.Id, out var existingThread))
            {
                logger.LogDebug("停止现有同步提供程序：{id}", syncRootInfo.Id);
                existingThread.Stop().Wait();
                _threads.Remove(syncRootInfo.Id);
            }

            var thread = new CancellableThread((cancellation) =>
                Run(syncRootInfo, cancellation), logger);

            thread.Stopped += (sender, e) =>
            {
                lock (_lock)
                {
                    _threads.Remove(syncRootInfo.Id);
                    (sender as CancellableThread)?.Dispose();
                }
            };

            thread.Start();
            _threads[syncRootInfo.Id] = thread;
            logger.LogDebug("已启动新的同步提供程序：{id}", syncRootInfo.Id);
        }
    }

    public bool Has(string id) => _threads.ContainsKey(id);

    public async Task StopAll()
    {
        _stopping = true;

        var stopTasks = _threads.Values.Select((thread) => thread.Stop()).ToArray();
        await Task.WhenAll(stopTasks);
    }

    public async Task StopSyncRoot(StorageProviderSyncRootInfo syncRootInfo)
    {
        try
        {
            if (_threads.TryGetValue(syncRootInfo.Id, out var existingThread))
            {
                logger.LogDebug("停止现有同步提供程序：{id}", syncRootInfo.Id);
                await existingThread.Stop();
                _threads.Remove(syncRootInfo.Id);
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "停止同步根失败");
        }
    }

    public async Task Stop(string id)
    {
        if (!_threads.TryGetValue(id, out var thread))
        {
            return;
        }
        await thread.Stop();
    }

    private async Task Run(StorageProviderSyncRootInfo syncRootInfo, CancellationToken cancellation)
    {
        try
        {
            logger.LogDebug("正在加载应用");
            logger.LogDebug("正在将同步提供程序连接到 {rootDirectory}", syncRootInfo.Path.Path);

            using var scope = scopeFactory.CreateScope();
            var contextAccessor = scope.ServiceProvider.GetRequiredService<SyncProviderContextAccessor>();
            contextAccessor.Context = new SyncProviderContext
            {
                Id = syncRootInfo.Id,
                RootDirectory = syncRootInfo.Path.Path,
                PopulationPolicy = (PopulationPolicy)syncRootInfo.PopulationPolicy,
            };

            // 验证远程上下文设置器
            var remoteContextSetters = scope.ServiceProvider.GetServices<IRemoteContextSetter>().ToList();
            logger.LogDebug("找到 {count} 个远程上下文设置器", remoteContextSetters.Count);

            var remoteContextSetter = remoteContextSetters
                .SingleOrDefault((setter) => setter.RemoteKind == contextAccessor.Context.RemoteKind);

            if (remoteContextSetter == null)
            {
                logger.LogError("未找到匹配的远程上下文设置器：{remoteKind}", contextAccessor.Context.RemoteKind);
                return;
            }

            remoteContextSetter.SetRemoteContext(syncRootInfo.Context.ToArray());

            var syncProvider = scope.ServiceProvider.GetRequiredService<SyncProvider>();
            await syncProvider.Run(cancellation);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "同步提供程序运行失败：{id}", syncRootInfo.Id);
            // 记录异常但不重新抛出，避免整个应用崩溃
        }
    }

    private sealed class CancellableThread : IDisposable
    {
        private readonly CancellationTokenSource _cts = new();
        private readonly Task _task;
        public event EventHandler? Stopped;

        public CancellableThread(Func<CancellationToken, Task> action, ILogger logger)
        {
            _task = new Task(async () =>
            {
                try
                {
                    await action(_cts.Token);
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "线程意外停止");
                }
                Stopped?.Invoke(this, EventArgs.Empty);
            });
        }

        public static CancellableThread CreateAndStart(Func<CancellationToken, Task> action, ILogger logger)
        {
            var cans = new CancellableThread(action, logger);
            cans.Start();
            return cans;
        }

        public void Start()
        {
            _task.Start();
        }

        public async Task Stop()
        {
            _cts.Cancel();
            await _task;

        }
        public void Dispose()
        {
            _cts.Cancel();
            _cts.Dispose();
        }
    }
}
