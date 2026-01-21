using NotifyRelay.Platforms.Windows.Abstractions;

namespace NotifyRelay.Platforms.Windows.RemoteStorage.Abstractions;
public interface ISyncProviderContextAccessor
{
    SyncProviderContext Context { get; }
}
