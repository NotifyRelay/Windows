using System.Diagnostics.CodeAnalysis;
using NotifyRelay.Platforms.Windows.Helpers;
using NotifyRelay.Platforms.Windows.RemoteStorage.RemoteAbstractions;

namespace NotifyRelay.Platforms.Windows.RemoteStorage.Worker.IO;

public static class RemoteDirectoryInfoExtensions
{
    public static int GetHashCode([DisallowNull] this RemoteDirectoryInfo obj) =>
        HashCode.Combine(
            // ignore sync attributes
            (int)obj.Attributes & ~SyncAttributes.ALL,
            obj.CreationTimeUtc,
            obj.LastWriteTimeUtc
        );
}
