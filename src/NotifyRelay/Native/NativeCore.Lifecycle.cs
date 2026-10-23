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

    public static int ImportState(string json)
    {
        return NotifyRelayCore.Safe.ImportState(_ctx, json);
    }

    public static string? DecryptLocalState(string encryptedB64, string deviceUuid)
    {
        return NotifyRelayCore.Safe.DecryptLocalState(_ctx, encryptedB64, deviceUuid);
    }

    public static string? SerializeLocalState(string deviceUuid, int encrypt)
    {
        return NotifyRelayCore.Safe.SerializeLocalState(_ctx, deviceUuid, encrypt);
    }

    /// <summary>本地状态反序列化聚合入口，返回 true 表示解密与导入均成功</summary>
    public static bool DeserializeLocalState(string payload, string deviceUuid, int encrypted)
    {
        return NotifyRelayCore.Safe.DeserializeLocalState(_ctx, payload, deviceUuid, encrypted);
    }
}
