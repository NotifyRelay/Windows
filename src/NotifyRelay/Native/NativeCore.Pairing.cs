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

    public static void SendAccept(string uuid, string ltPubKey, string ip, int battery, string deviceType)
    {
        NotifyRelayCore.Safe.SendAccept(_ctx, uuid, ltPubKey, ip, battery, deviceType);
    }

    public static void SendReject(string uuid)
    {
        NotifyRelayCore.Safe.SendReject(_ctx, uuid);
    }

}
