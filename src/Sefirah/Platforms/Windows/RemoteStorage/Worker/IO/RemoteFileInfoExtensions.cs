using NotifyRelay.Platforms.Windows.Helpers;
using NotifyRelay.Platforms.Windows.RemoteStorage.RemoteAbstractions;
using System.Diagnostics.CodeAnalysis;

namespace NotifyRelay.Platforms.Windows.RemoteStorage.Worker.IO;
public static class RemoteFileInfoExtensions
{
    public static int GetHashCode([DisallowNull] this RemoteFileInfo obj) =>
        HashCode.Combine(
            obj.Length,
            // ignore sync attributes
            (int)obj.Attributes & ~SyncAttributes.ALL,
            obj.CreationTimeUtc,
            obj.LastWriteTimeUtc
        );
}
