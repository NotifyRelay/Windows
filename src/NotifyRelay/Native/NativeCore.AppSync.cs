namespace NotifyRelay.Native;

public static partial class NativeCore
{
    // ======== Clipboard ========
    public static string? ClipboardOnChanged(string targetsJson, string mime, string content, bool force)
    {
        return NotifyRelayCore.Safe.ClipboardOnChanged(_ctx, _senderQueueHandle, targetsJson, mime, content, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), force);
    }

    public static string? ClipboardOnReceived(string payloadJson)
    {
        return NotifyRelayCore.Safe.ClipboardOnReceived(_ctx, payloadJson, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
    }

    // ======== App sync (app list & icons) ========
    public static string? AppSyncPrepareIconRequest(string packagesJson, string installedJson, string cachedJson, string appDeviceJson, string sourceDeviceUuid)
    {
        return NotifyRelayCore.Safe.AppSyncPrepareIconRequest(_ctx, packagesJson, installedJson, cachedJson, appDeviceJson, sourceDeviceUuid, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
    }

    public static void AppSyncClearIconPending(string packagesJson)
    {
        NotifyRelayCore.Safe.AppSyncClearIconPending(_ctx, packagesJson);
    }

    public static string? AppSyncParseIconResponse(string payloadJson)
    {
        return NotifyRelayCore.Safe.AppSyncParseIconResponse(payloadJson);
    }

    public static string? AppSyncBuildApplistRequest(string scope = "user")
    {
        return NotifyRelayCore.Safe.AppSyncBuildApplistRequest(scope, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
    }

    public static string? AppSyncParseApplistResponse(string payloadJson)
    {
        return NotifyRelayCore.Safe.AppSyncParseApplistResponse(payloadJson);
    }
}
