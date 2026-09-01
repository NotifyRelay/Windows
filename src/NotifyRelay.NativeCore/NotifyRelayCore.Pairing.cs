using System;
using System.Runtime.InteropServices;
using System.Text;

namespace NotifyRelay.Native;

public static partial class NotifyRelayCore
{
    // ======== Periodic broadcast & send functions ========
    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern int nrc_periodic_broadcast(IntPtr ctx, int action, IntPtr uuid, IntPtr name, int battery, IntPtr deviceType);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern int nrc_send_handshake(IntPtr ctx, IntPtr uuid, IntPtr pubKey, IntPtr localIp, IntPtr targetIp, int battery, IntPtr deviceType);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern int nrc_send_pairing_init(IntPtr ctx, IntPtr localUuid, IntPtr targetUuid, IntPtr expectedCode, int battery, IntPtr deviceType);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern int nrc_send_pairing_resp(IntPtr ctx, IntPtr uuid, IntPtr ltPub, IntPtr pairingCode, IntPtr ip, int battery, IntPtr deviceType);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern void nrc_send_accept(IntPtr ctx, IntPtr uuid, IntPtr ltPubKey, IntPtr ip, int battery, IntPtr deviceType);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern void nrc_send_reject(IntPtr ctx, IntPtr uuid);

    // ======== Pairing code management (Rust-generated) ========
    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr nrc_generate_pairing_code(IntPtr ctx, uint ttlSecs);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern void nrc_clear_pairing_code(IntPtr ctx);

    // ======== Device identity ========
    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr nrc_export_device_key(IntPtr ctx, IntPtr deviceUuid);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr nrc_get_local_uuid(IntPtr ctx);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern int nrc_rename_device(IntPtr ctx, IntPtr deviceUuid, IntPtr name);

    public static partial class Safe
    {
        // ======== Send functions ========
        public static int SendHandshake(IntPtr ctx, string uuid, string pubKey, string localIp, string targetIp, int battery, string deviceType)
        {
            var u = StringToPtr(uuid); var k = StringToPtr(pubKey); var li = StringToPtr(localIp); var ti = StringToPtr(targetIp); var d = StringToPtr(deviceType);
            var result = NotifyRelayCore.nrc_send_handshake(ctx, u, k, li, ti, battery, d);
            Marshal.FreeHGlobal(u); Marshal.FreeHGlobal(k); Marshal.FreeHGlobal(li); Marshal.FreeHGlobal(ti); Marshal.FreeHGlobal(d);
            return result;
        }

        public static int SendPairingInit(IntPtr ctx, string localUuid, string targetUuid, string expectedCode, int battery, string deviceType)
        {
            var lu = StringToPtr(localUuid); var tu = StringToPtr(targetUuid); var c = StringToPtr(expectedCode); var d = StringToPtr(deviceType);
            var result = NotifyRelayCore.nrc_send_pairing_init(ctx, lu, tu, c, battery, d);
            Marshal.FreeHGlobal(lu); Marshal.FreeHGlobal(tu); Marshal.FreeHGlobal(c); Marshal.FreeHGlobal(d);
            return result;
        }

        public static int SendPairingResp(IntPtr ctx, string uuid, string ltPub, string pairingCode, string ip, int battery, string deviceType)
        {
            var u = StringToPtr(uuid); var l = StringToPtr(ltPub); var c = StringToPtr(pairingCode); var i = StringToPtr(ip); var d = StringToPtr(deviceType);
            var result = NotifyRelayCore.nrc_send_pairing_resp(ctx, u, l, c, i, battery, d);
            Marshal.FreeHGlobal(u); Marshal.FreeHGlobal(l); Marshal.FreeHGlobal(c); Marshal.FreeHGlobal(i); Marshal.FreeHGlobal(d);
            return result;
        }

        public static void SendAccept(IntPtr ctx, string uuid, string ltPubKey, string ip, int battery, string deviceType)
        {
            var u = StringToPtr(uuid); var k = StringToPtr(ltPubKey); var i = StringToPtr(ip); var d = StringToPtr(deviceType);
            NotifyRelayCore.nrc_send_accept(ctx, u, k, i, battery, d);
            Marshal.FreeHGlobal(u); Marshal.FreeHGlobal(k); Marshal.FreeHGlobal(i); Marshal.FreeHGlobal(d);
        }

        public static void SendReject(IntPtr ctx, string uuid)
        {
            var u = StringToPtr(uuid);
            NotifyRelayCore.nrc_send_reject(ctx, u);
            Marshal.FreeHGlobal(u);
        }

        public static int PeriodicBroadcast(IntPtr ctx, int action, string? uuid, string? name, int battery, string? deviceType)
        {
            var u = StringToPtr(uuid); var n = StringToPtr(name); var d = StringToPtr(deviceType);
            var result = NotifyRelayCore.nrc_periodic_broadcast(ctx, action, u, n, battery, d);
            Marshal.FreeHGlobal(u); Marshal.FreeHGlobal(n); Marshal.FreeHGlobal(d);
            return result;
        }

        // ======== Pairing code management (Rust-generated) ========
        public static string? GeneratePairingCode(IntPtr ctx, uint ttlSecs = 300)
        {
            return PtrToStringAndFree(NotifyRelayCore.nrc_generate_pairing_code(ctx, ttlSecs));
        }

        public static void ClearPairingCode(IntPtr ctx)
        {
            NotifyRelayCore.nrc_clear_pairing_code(ctx);
        }

        // ======== Device identity ========
        public static string? ExportDeviceKey(IntPtr ctx, string deviceUuid)
        {
            var u = StringToPtr(deviceUuid);
            var result = NotifyRelayCore.nrc_export_device_key(ctx, u);
            Marshal.FreeHGlobal(u);
            return PtrToStringAndFree(result);
        }

        public static string? GetLocalUuid(IntPtr ctx)
        {
            return PtrToStringAndFree(NotifyRelayCore.nrc_get_local_uuid(ctx));
        }

        public static int RenameDevice(IntPtr ctx, string deviceUuid, string name)
        {
            var u = StringToPtr(deviceUuid);
            var n = StringToPtr(name);
            var result = NotifyRelayCore.nrc_rename_device(ctx, u, n);
            Marshal.FreeHGlobal(u);
            Marshal.FreeHGlobal(n);
            return result;
        }
    }
}
