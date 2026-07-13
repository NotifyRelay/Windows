using System.Runtime.InteropServices;
using System.Text;

namespace NotifyRelay.Native;

public static class NotifyRelayCore
{
    private const string DllName = "notify_relay_core";

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public delegate void MessageCallback(IntPtr type, IntPtr data, IntPtr userData);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public delegate void PairingCallback(IntPtr type, IntPtr uuid, IntPtr pubKey, IntPtr userData);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr nrc_init();

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern void nrc_destroy(IntPtr ctx);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern int nrc_ecdh_generate_keypair(IntPtr ctx);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr nrc_ecdh_get_public_key(IntPtr ctx);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern int nrc_ecdh_has_keypair(IntPtr ctx);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern int nrc_ecdh_derive_shared_secret(IntPtr ctx, IntPtr peerUuid, IntPtr peerPubKeyB64);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern int nrc_migrate_shared_secret(IntPtr ctx, IntPtr deviceUuid, [In] byte[] aesKey, uint len);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern int nrc_remove_device(IntPtr ctx, IntPtr deviceUuid);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr nrc_encrypt_message(IntPtr ctx, IntPtr header, IntPtr localUuid, IntPtr localPubKey, IntPtr remoteUuid, IntPtr plaintext);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr nrc_decrypt_message(IntPtr ctx, IntPtr encryptedLine);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern int nrc_process_line(IntPtr ctx, IntPtr line, MessageCallback? onMessage, PairingCallback? onPairing, IntPtr userData);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr nrc_format_heartbeat(IntPtr uuid, IntPtr name, ushort port, int battery, IntPtr deviceType);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr nrc_parse_heartbeat(IntPtr line);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr nrc_format_discovery(IntPtr uuid, IntPtr name, ushort port, int battery, IntPtr deviceType);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr nrc_format_tcp_heartbeat(IntPtr uuid, IntPtr name, ushort port, int battery, IntPtr deviceType);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr nrc_parse_heartbeat_json(IntPtr line);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr nrc_parse_heartbeat_tcp_json(IntPtr line);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr nrc_format_pairing_init(IntPtr uuid, IntPtr tmpPubKey, IntPtr ip, int battery, IntPtr deviceType);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr nrc_format_pairing_resp(IntPtr uuid, IntPtr tmpPub, IntPtr ltPub, IntPtr encryptedCode, IntPtr ip, int battery, IntPtr deviceType);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr nrc_format_accept(IntPtr uuid, IntPtr ltPubKey, IntPtr ip, int battery, IntPtr deviceType);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr nrc_format_handshake(IntPtr uuid, IntPtr pubKey, IntPtr ip, int battery, IntPtr deviceType);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr nrc_export_state(IntPtr ctx);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern int nrc_import_state(IntPtr ctx, IntPtr json);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern void nrc_free_string(IntPtr s);

    public static string? PtrToStringAndFree(IntPtr ptr)
    {
        if (ptr == IntPtr.Zero) return null;
        var result = Marshal.PtrToStringUTF8(ptr);
        nrc_free_string(ptr);
        return result;
    }

    private static IntPtr StringToPtr(string? s)
    {
        if (s == null) return IntPtr.Zero;
        var bytes = Encoding.UTF8.GetBytes(s);
        var ptr = Marshal.AllocHGlobal(bytes.Length + 1);
        Marshal.Copy(bytes, 0, ptr, bytes.Length);
        Marshal.WriteByte(ptr, bytes.Length, 0);
        return ptr;
    }

    public static class Safe
    {
        public static int MigrateSharedSecret(IntPtr ctx, string deviceUuid, byte[] aesKey)
        {
            var u = StringToPtr(deviceUuid);
            var result = NotifyRelayCore.nrc_migrate_shared_secret(ctx, u, aesKey, (uint)aesKey.Length);
            Marshal.FreeHGlobal(u);
            return result;
        }

        public static int RemoveDevice(IntPtr ctx, string deviceUuid)
        {
            var u = StringToPtr(deviceUuid);
            var result = NotifyRelayCore.nrc_remove_device(ctx, u);
            Marshal.FreeHGlobal(u);
            return result;
        }

        public static string? EncryptMessage(IntPtr ctx, string header, string localUuid, string localPubKey, string remoteUuid, string plaintext)
        {
            var h = StringToPtr(header);
            var u = StringToPtr(localUuid);
            var k = StringToPtr(localPubKey);
            var r = StringToPtr(remoteUuid);
            var p = StringToPtr(plaintext);
            var result = NotifyRelayCore.nrc_encrypt_message(ctx, h, u, k, r, p);
            Marshal.FreeHGlobal(h);
            Marshal.FreeHGlobal(u);
            Marshal.FreeHGlobal(k);
            Marshal.FreeHGlobal(r);
            Marshal.FreeHGlobal(p);
            return PtrToStringAndFree(result);
        }

        public static string? DecryptMessage(IntPtr ctx, string encryptedLine)
        {
            var line = StringToPtr(encryptedLine);
            var result = NotifyRelayCore.nrc_decrypt_message(ctx, line);
            Marshal.FreeHGlobal(line);
            return PtrToStringAndFree(result);
        }

        public static int ProcessLine(IntPtr ctx, string line, MessageCallback? onMsg, PairingCallback? onPair, IntPtr userData)
        {
            var linePtr = StringToPtr(line);
            var result = NotifyRelayCore.nrc_process_line(ctx, linePtr, onMsg, onPair, userData);
            Marshal.FreeHGlobal(linePtr);
            return result;
        }

        public static int DeriveSharedSecret(IntPtr ctx, string peerUuid, string peerPubKeyB64)
        {
            var u = StringToPtr(peerUuid);
            var k = StringToPtr(peerPubKeyB64);
            var result = NotifyRelayCore.nrc_ecdh_derive_shared_secret(ctx, u, k);
            Marshal.FreeHGlobal(u);
            Marshal.FreeHGlobal(k);
            return result;
        }

        public static string? ExportState(IntPtr ctx)
        {
            return PtrToStringAndFree(nrc_export_state(ctx));
        }

        public static string? FormatHeartbeat(string uuid, string nameB64, ushort port, int battery, string deviceType)
        {
            var u = StringToPtr(uuid);
            var n = StringToPtr(nameB64);
            var d = StringToPtr(deviceType);
            var result = NotifyRelayCore.nrc_format_heartbeat(u, n, port, battery, d);
            Marshal.FreeHGlobal(u);
            Marshal.FreeHGlobal(n);
            Marshal.FreeHGlobal(d);
            return PtrToStringAndFree(result);
        }

        public static string? FormatDiscovery(string uuid, string nameB64, ushort port, int battery, string deviceType)
        {
            var u = StringToPtr(uuid);
            var n = StringToPtr(nameB64);
            var d = StringToPtr(deviceType);
            var result = NotifyRelayCore.nrc_format_discovery(u, n, port, battery, d);
            Marshal.FreeHGlobal(u);
            Marshal.FreeHGlobal(n);
            Marshal.FreeHGlobal(d);
            return PtrToStringAndFree(result);
        }

        public static int ImportState(IntPtr ctx, string json)
        {
            var jsonPtr = StringToPtr(json);
            var result = nrc_import_state(ctx, jsonPtr);
            Marshal.FreeHGlobal(jsonPtr);
            return result;
        }

        public static string? FormatTcpHeartbeat(string uuid, string nameB64, ushort port, int battery, string deviceType)
        {
            var u = StringToPtr(uuid);
            var n = StringToPtr(nameB64);
            var d = StringToPtr(deviceType);
            var result = NotifyRelayCore.nrc_format_tcp_heartbeat(u, n, port, battery, d);
            Marshal.FreeHGlobal(u);
            Marshal.FreeHGlobal(n);
            Marshal.FreeHGlobal(d);
            return PtrToStringAndFree(result);
        }

        public static string? ParseHeartbeatJson(string line)
        {
            var l = StringToPtr(line);
            var result = NotifyRelayCore.nrc_parse_heartbeat_json(l);
            Marshal.FreeHGlobal(l);
            return PtrToStringAndFree(result);
        }

        public static string? ParseHeartbeatTcpJson(string line)
        {
            var l = StringToPtr(line);
            var result = NotifyRelayCore.nrc_parse_heartbeat_tcp_json(l);
            Marshal.FreeHGlobal(l);
            return PtrToStringAndFree(result);
        }

        public static string? FormatPairingInit(string uuid, string tmpPubKey, string ip, int battery, string deviceType)
        {
            var u = StringToPtr(uuid);
            var k = StringToPtr(tmpPubKey);
            var i = StringToPtr(ip);
            var d = StringToPtr(deviceType);
            var result = NotifyRelayCore.nrc_format_pairing_init(u, k, i, battery, d);
            Marshal.FreeHGlobal(u);
            Marshal.FreeHGlobal(k);
            Marshal.FreeHGlobal(i);
            Marshal.FreeHGlobal(d);
            return PtrToStringAndFree(result);
        }

        public static string? FormatPairingResp(string uuid, string tmpPub, string ltPub, string encryptedCode, string ip, int battery, string deviceType)
        {
            var u = StringToPtr(uuid);
            var t = StringToPtr(tmpPub);
            var l = StringToPtr(ltPub);
            var e = StringToPtr(encryptedCode);
            var i = StringToPtr(ip);
            var d = StringToPtr(deviceType);
            var result = NotifyRelayCore.nrc_format_pairing_resp(u, t, l, e, i, battery, d);
            Marshal.FreeHGlobal(u);
            Marshal.FreeHGlobal(t);
            Marshal.FreeHGlobal(l);
            Marshal.FreeHGlobal(e);
            Marshal.FreeHGlobal(i);
            Marshal.FreeHGlobal(d);
            return PtrToStringAndFree(result);
        }

        public static string? FormatAccept(string uuid, string ltPubKey, string ip, int battery, string deviceType)
        {
            var u = StringToPtr(uuid);
            var k = StringToPtr(ltPubKey);
            var i = StringToPtr(ip);
            var d = StringToPtr(deviceType);
            var result = NotifyRelayCore.nrc_format_accept(u, k, i, battery, d);
            Marshal.FreeHGlobal(u);
            Marshal.FreeHGlobal(k);
            Marshal.FreeHGlobal(i);
            Marshal.FreeHGlobal(d);
            return PtrToStringAndFree(result);
        }

        public static string? FormatHandshake(string uuid, string pubKey, string ip, int battery, string deviceType)
        {
            var u = StringToPtr(uuid);
            var k = StringToPtr(pubKey);
            var i = StringToPtr(ip);
            var d = StringToPtr(deviceType);
            var result = NotifyRelayCore.nrc_format_handshake(u, k, i, battery, d);
            Marshal.FreeHGlobal(u);
            Marshal.FreeHGlobal(k);
            Marshal.FreeHGlobal(i);
            Marshal.FreeHGlobal(d);
            return PtrToStringAndFree(result);
        }
    }
}
