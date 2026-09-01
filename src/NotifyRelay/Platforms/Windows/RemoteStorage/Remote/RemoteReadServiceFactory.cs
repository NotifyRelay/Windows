using NotifyRelay.Platforms.Windows.RemoteStorage.Abstractions;
using NotifyRelay.Platforms.Windows.RemoteStorage.RemoteAbstractions;

namespace NotifyRelay.Platforms.Windows.RemoteStorage.Remote;

public class RemoteReadServiceFactory(SyncProviderContextAccessor contextAccessor, IEnumerable<LazyRemote<IRemoteReadService>> options)
    : RemoteFactory<IRemoteReadService>(contextAccessor, options)
{ }
