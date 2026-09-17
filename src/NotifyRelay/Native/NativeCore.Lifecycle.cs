namespace NotifyRelay.Native;

public static partial class NativeCore
{
    // ======== Network layer wrappers ========

    public static long StartCore(string uuid, string name, int battery, string deviceType, ushort tcpPort, string pubKey, ulong heartbeatIntervalMs = 2000, long offlineTimeoutSec = 12, ulong offlineCheckIntervalMs = 5000, ulong reconnectIntervalSecs = 10, uint reconnectMaxRetries = 5)
    {
        _senderQueueHandle = NotifyRelayCore.Safe.StartCore(_ctx, uuid, name, battery, deviceType, tcpPort, pubKey, heartbeatIntervalMs, offlineTimeoutSec, offlineCheckIntervalMs, reconnectIntervalSecs, reconnectMaxRetries);
        return _senderQueueHandle;
    }

    public static int RemoveDeviceSession(string uuid)
    {
        return NotifyRelayCore.Safe.RemoveDeviceSession(_ctx, uuid);
    }

    // ======== New function wrappers ========

    public static string? ComputeDedupKey(string deviceUuid, string data)
    {
        return NotifyRelayCore.Safe.ComputeDedupKey(deviceUuid, data);
    }

    public static string? ComputeFeatureId(string superPkg, string paramV2Raw, string title, string text, string instanceId)
    {
        return NotifyRelayCore.Safe.ComputeFeatureId(superPkg, paramV2Raw, title, text, instanceId);
    }

    public static string? ExportState()
    {
        return NotifyRelayCore.Safe.ExportState(_ctx);
    }

    public static int ImportState(string json)
    {
        return NotifyRelayCore.Safe.ImportState(_ctx, json);
    }

    public static string? EncryptLocalState(string plaintext, string deviceUuid)
    {
        return NotifyRelayCore.Safe.EncryptLocalState(_ctx, plaintext, deviceUuid);
    }

    public static string? DecryptLocalState(string encryptedB64, string deviceUuid)
    {
        return NotifyRelayCore.Safe.DecryptLocalState(_ctx, encryptedB64, deviceUuid);
    }
}
