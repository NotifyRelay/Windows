using System.Runtime.InteropServices;

namespace NotifyRelay.Native;

public static partial class NativeCore
{
    // ======== Network change ========
    public static void OnNetworkChanged(string? localIp = null)
    {
        if (localIp is not null)
        {
            var bytes = System.Text.Encoding.UTF8.GetBytes(localIp);
            var ipPtr = Marshal.AllocHGlobal(bytes.Length + 1);
            Marshal.Copy(bytes, 0, ipPtr, bytes.Length);
            Marshal.WriteByte(ipPtr, bytes.Length, 0);
            NotifyRelayCore.nrc_on_network_changed(_ctx, ipPtr);
            Marshal.FreeHGlobal(ipPtr);
        }
        else
        {
            NotifyRelayCore.nrc_on_network_changed(_ctx, IntPtr.Zero);
        }
    }

    // ======== Local IP ========
    public static string? GetLocalIp()
    {
        return NotifyRelayCore.PtrToStringAndFree(NotifyRelayCore.nrc_get_local_ip());
    }

    // ======== Discovery ========
    public static void AddKnownDevice(string uuid, string ip)
    {
        NotifyRelayCore.Safe.AddKnownDevice(_ctx, uuid, ip);
    }

    public static void RemoveKnownDevice(string uuid)
    {
        NotifyRelayCore.Safe.RemoveKnownDevice(_ctx, uuid);
    }
}
