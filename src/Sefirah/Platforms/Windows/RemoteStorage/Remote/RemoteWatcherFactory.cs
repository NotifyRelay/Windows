using NotifyRelay.Platforms.Windows.RemoteStorage.Abstractions;
using NotifyRelay.Platforms.Windows.RemoteStorage.RemoteAbstractions;

namespace NotifyRelay.Platforms.Windows.RemoteStorage.Remote;

public class RemoteWatcherFactory(SyncProviderContextAccessor contextAccessor, IEnumerable<LazyRemote<IRemoteWatcher>> options)
    : RemoteFactory<IRemoteWatcher>(contextAccessor, options)
{ }
