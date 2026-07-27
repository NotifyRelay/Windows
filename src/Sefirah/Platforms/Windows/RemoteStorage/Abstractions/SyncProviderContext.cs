using NotifyRelay.Platforms.Windows.RemoteStorage.Commands;

namespace NotifyRelay.Platforms.Windows.RemoteStorage.Abstractions;

public partial record SyncProviderContext
{
    public required string Id { get; init; }
    public required string RootDirectory { get; init; }
    public required PopulationPolicy PopulationPolicy { get; init; }
    public string AccountId => Id.Split('!', 3)[2];
    public string RemoteKind => "ftp";
}
