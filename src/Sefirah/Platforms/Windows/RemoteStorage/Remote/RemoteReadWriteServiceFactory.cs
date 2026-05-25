using NotifyRelay.Platforms.Windows.RemoteStorage.Abstractions;
using NotifyRelay.Platforms.Windows.RemoteStorage.RemoteAbstractions;

namespace NotifyRelay.Platforms.Windows.RemoteStorage.Remote;

public class RemoteReadWriteServiceFactory(SyncProviderContextAccessor contextAccessor, IEnumerable<LazyRemote<IRemoteReadWriteService>> options)
    : RemoteFactory<IRemoteReadWriteService>(contextAccessor, options)
{ }
