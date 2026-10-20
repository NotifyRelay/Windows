namespace NotifyRelay.Native;

public static partial class NativeCore
{
    public static int PeriodicBroadcast(int action, string? uuid = null, string? name = null, int battery = -1, string? deviceType = null)
    {
        return NotifyRelayCore.Safe.PeriodicBroadcast(_ctx, action, uuid, name, battery, deviceType);
    }

    public static void SendPairingResp(string uuid, string ltPub, string pairingCode, string ip, int battery, string deviceType)
    {
        NotifyRelayCore.Safe.SendPairingResp(_ctx, uuid, ltPub, pairingCode, ip, battery, deviceType);
    }

    /// <summary>作为发起端发起配对：内部完成 SPAKE2 协商、发送 PAIRING_INIT、等待 PAIRING_RESP 与回传 ACCEPT。</summary>
    /// <param name="targetUuid">对端 uuid（core 用 device_ips 表解析目标 IP）。</param>
    /// <param name="expectedCode">本机展示、由对端输入的 6 位配对码。</param>
    /// <returns>0=已发送（结果经 on_pairing 的 RESULT 回调上抛）；-1=参数无效、无目标 IP、连接或收发失败。</returns>
    public static int SendPairingInit(string localUuid, string targetUuid, string expectedCode, int battery, string deviceType)
    {
        return NotifyRelayCore.Safe.SendPairingInit(_ctx, localUuid, targetUuid, expectedCode, battery, deviceType);
    }

    public static string? GeneratePairingCode(uint ttlSecs = 300)
    {
        return NotifyRelayCore.Safe.GeneratePairingCode(_ctx, ttlSecs);
    }

    public static void ClearPairingCode()
    {
        NotifyRelayCore.Safe.ClearPairingCode(_ctx);
    }

    public static void SendAccept(string uuid, string ltPubKey, string ip, int battery, string deviceType)
    {
        NotifyRelayCore.Safe.SendAccept(_ctx, uuid, ltPubKey, ip, battery, deviceType);
    }

    public static void SendReject(string uuid)
    {
        NotifyRelayCore.Safe.SendReject(_ctx, uuid);
    }

}
