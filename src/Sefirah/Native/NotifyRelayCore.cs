using System.Runtime.InteropServices;
using System.Text;

namespace NotifyRelay.Native;

public static class NotifyRelayCore
{
    private const string DllName = "notify_relay_core";

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
    public static extern IntPtr nrc_decrypt_payload(IntPtr ctx, IntPtr localUuid, IntPtr encryptedB64);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr nrc_decode_line(IntPtr ctx, IntPtr line);

    // ======== Format helpers (for one-shot sync socket ops) ========
    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr nrc_format_handshake(IntPtr uuid, IntPtr pubKey, IntPtr ip, int battery, IntPtr deviceType);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr nrc_format_pairing_init(IntPtr uuid, IntPtr tmpPubKey, IntPtr ip, int battery, IntPtr deviceType);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr nrc_format_pairing_resp(IntPtr uuid, IntPtr tmpPub, IntPtr ltPub, IntPtr encryptedCode, IntPtr ip, int battery, IntPtr deviceType);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr nrc_format_accept(IntPtr uuid, IntPtr ltPubKey, IntPtr ip, int battery, IntPtr deviceType);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr nrc_format_heartbeat(IntPtr uuid, IntPtr name, ushort port, int battery, IntPtr deviceType);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr nrc_format_discovery(IntPtr uuid, IntPtr name, ushort port, int battery, IntPtr deviceType);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr nrc_format_tcp_heartbeat(IntPtr uuid, IntPtr name, ushort port, int battery, IntPtr deviceType);

    // ======== Send functions (for established connections) ========
    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern void nrc_send_handshake(IntPtr ctx, IntPtr uuid, IntPtr pubKey, IntPtr ip, int battery, IntPtr deviceType);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern void nrc_send_pairing_init(IntPtr ctx, IntPtr uuid, IntPtr ip, int battery, IntPtr deviceType);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern void nrc_send_pairing_resp(IntPtr ctx, IntPtr uuid, IntPtr ltPub, IntPtr pairingCode, IntPtr ip, int battery, IntPtr deviceType);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern void nrc_send_accept(IntPtr ctx, IntPtr uuid, IntPtr ltPubKey, IntPtr ip, int battery, IntPtr deviceType);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern void nrc_send_reject(IntPtr ctx, IntPtr uuid);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern void nrc_send_heartbeat_tcp(IntPtr ctx, IntPtr uuid, IntPtr name, ushort port, int battery, IntPtr deviceType);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern void nrc_send_heartbeat_udp(IntPtr ctx, IntPtr uuid, IntPtr name, ushort port, int battery, IntPtr deviceType);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern void nrc_send_discovery(IntPtr ctx, IntPtr uuid, IntPtr name, ushort port, int battery, IntPtr deviceType);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern void nrc_send_data_message(IntPtr ctx, IntPtr header, IntPtr localUuid, IntPtr localPubKey, IntPtr remoteUuid, IntPtr plaintext);

    // ======== UDP broadcast processing ========
    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern int nrc_process_udp_broadcast(IntPtr ctx, IntPtr line);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr nrc_export_state(IntPtr ctx);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern int nrc_import_state(IntPtr ctx, IntPtr json);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr nrc_encrypt_local_state(IntPtr ctx, IntPtr plaintext, IntPtr deviceUuid);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr nrc_decrypt_local_state(IntPtr ctx, IntPtr encryptedB64, IntPtr deviceUuid);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern void nrc_free_string(IntPtr s);

    // ======== New: Ephemeral ECDH ========
    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern int nrc_ecdh_generate_ephemeral_keypair(IntPtr ctx);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr nrc_ecdh_get_ephemeral_public_key(IntPtr ctx);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern int nrc_ecdh_has_ephemeral_keypair(IntPtr ctx);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern void nrc_ecdh_clear_ephemeral_keypair(IntPtr ctx);

    // ======== New: Pairing code ========
    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern int nrc_ecdh_derive_pairing_key(IntPtr ctx, IntPtr peerEphPubB64);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr nrc_ecdh_encrypt_pairing_code(IntPtr ctx, IntPtr code);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr nrc_ecdh_decrypt_pairing_code(IntPtr ctx, IntPtr encryptedB64);

    // ======== New: Long-term key alias ========
    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern int nrc_ecdh_derive_long_term_key(IntPtr ctx, IntPtr peerUuid, IntPtr peerLtPubB64);

    // ======== New: Key export ========
    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr nrc_export_device_key(IntPtr ctx, IntPtr deviceUuid);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr nrc_export_local_keypair(IntPtr ctx);

    // ======== New: Unified process ========
    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern int nrc_process_line(IntPtr ctx, IntPtr line);

    // ======== New: User data ========
    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern void nrc_set_user_data(IntPtr ctx, IntPtr userData);

    // ======== Callback delegate types ========
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public delegate void OnHandshakeCb(IntPtr uuid, IntPtr pubKey, IntPtr ip, int battery, IntPtr deviceType, IntPtr userData);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public delegate void OnPairingInitCb(IntPtr uuid, IntPtr tmpPubKey, IntPtr ip, int battery, IntPtr deviceType, IntPtr userData);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public delegate void OnPairingRespCb(IntPtr uuid, IntPtr tmpPub, IntPtr ltPub, IntPtr encryptedCode, IntPtr ip, int battery, IntPtr deviceType, IntPtr userData);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public delegate void OnAcceptCb(IntPtr uuid, IntPtr ltPubKey, IntPtr ip, int battery, IntPtr deviceType, IntPtr userData);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public delegate void OnRejectCb(IntPtr uuid, IntPtr userData);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public delegate void OnHeartbeatTcpCb(IntPtr uuid, IntPtr nameB64, ushort port, int battery, IntPtr deviceType, IntPtr ip, IntPtr userData);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public delegate void OnDataCb(IntPtr localUuid, IntPtr plaintext, IntPtr userData);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public delegate void OnLogCb(int level, IntPtr message);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public delegate void OnHeartbeatUdpCb(IntPtr uuid, IntPtr nameB64, ushort port, int battery, IntPtr deviceType, IntPtr userData);

    // ======== Callback setters ========
    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern void nrc_set_log_callback(IntPtr cb);
    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern void nrc_set_on_handshake_cb(IntPtr ctx, OnHandshakeCb cb);
    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern void nrc_set_on_pairing_init_cb(IntPtr ctx, OnPairingInitCb cb);
    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern void nrc_set_on_pairing_resp_cb(IntPtr ctx, OnPairingRespCb cb);
    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern void nrc_set_on_accept_cb(IntPtr ctx, OnAcceptCb cb);
    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern void nrc_set_on_reject_cb(IntPtr ctx, OnRejectCb cb);
    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern void nrc_set_on_heartbeat_tcp_cb(IntPtr ctx, OnHeartbeatTcpCb cb);
    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern void nrc_set_on_notification_cb(IntPtr ctx, OnDataCb cb);
    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern void nrc_set_on_media_play_cb(IntPtr ctx, OnDataCb cb);
    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern void nrc_set_on_icon_request_cb(IntPtr ctx, OnDataCb cb);
    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern void nrc_set_on_icon_response_cb(IntPtr ctx, OnDataCb cb);
    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern void nrc_set_on_app_list_request_cb(IntPtr ctx, OnDataCb cb);
    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern void nrc_set_on_app_list_response_cb(IntPtr ctx, OnDataCb cb);
    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern void nrc_set_on_media_control_cb(IntPtr ctx, OnDataCb cb);
    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern void nrc_set_on_ftp_cb(IntPtr ctx, OnDataCb cb);
    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern void nrc_set_on_clipboard_cb(IntPtr ctx, OnDataCb cb);
    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern void nrc_set_on_status_cb(IntPtr ctx, OnDataCb cb);
    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern void nrc_set_on_app_launch_cb(IntPtr ctx, OnDataCb cb);
    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern void nrc_set_on_superisland_cb(IntPtr ctx, OnDataCb cb);
    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern void nrc_set_on_unknown_data_cb(IntPtr ctx, OnDataCb cb);
    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern void nrc_set_on_heartbeat_udp_cb(IntPtr ctx, OnHeartbeatUdpCb cb);

    // ======== Heartbeat tick & device timeout ========
    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern int nrc_heartbeat_tick(IntPtr ctx, long timeoutSec);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern void nrc_set_on_device_timeout_cb(IntPtr ctx, OnDeviceTimeoutCb cb);

    public delegate void OnDeviceTimeoutCb(IntPtr uuid, IntPtr userData);

    // ======== Network layer ========
    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern int nrc_start_tcp_server(IntPtr ctx, ushort port);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern int nrc_stop_tcp_server(IntPtr ctx);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern int nrc_send_to_device(IntPtr ctx, IntPtr uuid, IntPtr message);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern int nrc_broadcast_message(IntPtr ctx, IntPtr message);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern int nrc_get_connected_device_count(IntPtr ctx);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern int nrc_is_device_connected(IntPtr ctx, IntPtr uuid);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern int nrc_remove_device_session(IntPtr ctx, IntPtr uuid);

    // ======== Network callbacks ========
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public delegate void OnDeviceConnectedCb(IntPtr uuid, IntPtr ip, IntPtr userData);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public delegate void OnDeviceDisconnectedCb(IntPtr uuid, IntPtr userData);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public delegate void OnTcpErrorCb(IntPtr error, IntPtr userData);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern void nrc_set_on_device_connected_cb(IntPtr ctx, OnDeviceConnectedCb cb);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern void nrc_set_on_device_disconnected_cb(IntPtr ctx, OnDeviceDisconnectedCb cb);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern void nrc_set_on_tcp_error_cb(IntPtr ctx, OnTcpErrorCb cb);

    // ======== New: verify_pairing_code, compute_dedup_key, heartbeat_timeout, feature_id ========
    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern int nrc_verify_pairing_code(IntPtr storedCode, IntPtr inputCode);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr nrc_compute_dedup_key(IntPtr deviceUuid, IntPtr data);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern int nrc_heartbeat_has_timed_out(long lastHeartbeatSec, long nowSec, long timeoutSec);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr nrc_compute_feature_id(IntPtr packageName, IntPtr title, IntPtr text);

    // ======== Heartbeat parse with callback (eliminates JSON re-parse) ========
    public delegate void OnHeartbeatWithCb(IntPtr uuid, IntPtr nameB64, ushort port, int battery, IntPtr deviceType, IntPtr userData);
    public delegate void OnHeartbeatTcpWithCb(IntPtr uuid, IntPtr nameB64, ushort port, int battery, IntPtr deviceType, IntPtr ip, IntPtr userData);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern int nrc_parse_heartbeat_with_cb(IntPtr line, OnHeartbeatWithCb cb, IntPtr userData);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern int nrc_parse_heartbeat_tcp_with_cb(IntPtr line, OnHeartbeatTcpWithCb cb, IntPtr userData);

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

        public static string? DecryptPayload(IntPtr ctx, string localUuid, string encryptedB64)
        {
            var u = StringToPtr(localUuid);
            var e = StringToPtr(encryptedB64);
            var result = NotifyRelayCore.nrc_decrypt_payload(ctx, u, e);
            Marshal.FreeHGlobal(u);
            Marshal.FreeHGlobal(e);
            return PtrToStringAndFree(result);
        }

        public static string? DecodeLine(IntPtr ctx, string line)
        {
            var linePtr = StringToPtr(line);
            var result = NotifyRelayCore.nrc_decode_line(ctx, linePtr);
            Marshal.FreeHGlobal(linePtr);
            return PtrToStringAndFree(result);
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

        public static int ProcessUdpBroadcast(IntPtr ctx, string line)
        {
            var l = StringToPtr(line);
            var result = NotifyRelayCore.nrc_process_udp_broadcast(ctx, l);
            Marshal.FreeHGlobal(l);
            return result;
        }

        public static void SendHandshake(IntPtr ctx, string uuid, string pubKey, string ip, int battery, string deviceType)
        {
            var u = StringToPtr(uuid); var k = StringToPtr(pubKey); var i = StringToPtr(ip); var d = StringToPtr(deviceType);
            NotifyRelayCore.nrc_send_handshake(ctx, u, k, i, battery, d);
            Marshal.FreeHGlobal(u); Marshal.FreeHGlobal(k); Marshal.FreeHGlobal(i); Marshal.FreeHGlobal(d);
        }

        public static void SendPairingInit(IntPtr ctx, string uuid, string ip, int battery, string deviceType)
        {
            var u = StringToPtr(uuid); var i = StringToPtr(ip); var d = StringToPtr(deviceType);
            NotifyRelayCore.nrc_send_pairing_init(ctx, u, i, battery, d);
            Marshal.FreeHGlobal(u); Marshal.FreeHGlobal(i); Marshal.FreeHGlobal(d);
        }

        public static void SendPairingResp(IntPtr ctx, string uuid, string ltPub, string pairingCode, string ip, int battery, string deviceType)
        {
            var u = StringToPtr(uuid); var l = StringToPtr(ltPub); var c = StringToPtr(pairingCode); var i = StringToPtr(ip); var d = StringToPtr(deviceType);
            NotifyRelayCore.nrc_send_pairing_resp(ctx, u, l, c, i, battery, d);
            Marshal.FreeHGlobal(u); Marshal.FreeHGlobal(l); Marshal.FreeHGlobal(c); Marshal.FreeHGlobal(i); Marshal.FreeHGlobal(d);
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

        public static void SendHeartbeatTcp(IntPtr ctx, string uuid, string name, ushort port, int battery, string deviceType)
        {
            var u = StringToPtr(uuid); var n = StringToPtr(name); var d = StringToPtr(deviceType);
            NotifyRelayCore.nrc_send_heartbeat_tcp(ctx, u, n, port, battery, d);
            Marshal.FreeHGlobal(u); Marshal.FreeHGlobal(n); Marshal.FreeHGlobal(d);
        }

        public static void SendHeartbeatUdp(IntPtr ctx, string uuid, string name, ushort port, int battery, string deviceType)
        {
            var u = StringToPtr(uuid); var n = StringToPtr(name); var d = StringToPtr(deviceType);
            NotifyRelayCore.nrc_send_heartbeat_udp(ctx, u, n, port, battery, d);
            Marshal.FreeHGlobal(u); Marshal.FreeHGlobal(n); Marshal.FreeHGlobal(d);
        }

        public static void SendDiscovery(IntPtr ctx, string uuid, string name, ushort port, int battery, string deviceType)
        {
            var u = StringToPtr(uuid); var n = StringToPtr(name); var d = StringToPtr(deviceType);
            NotifyRelayCore.nrc_send_discovery(ctx, u, n, port, battery, d);
            Marshal.FreeHGlobal(u); Marshal.FreeHGlobal(n); Marshal.FreeHGlobal(d);
        }

        public static void SendDataMessage(IntPtr ctx, string header, string localUuid, string localPubKey, string remoteUuid, string plaintext)
        {
            var h = StringToPtr(header); var u = StringToPtr(localUuid); var k = StringToPtr(localPubKey); var r = StringToPtr(remoteUuid); var p = StringToPtr(plaintext);
            NotifyRelayCore.nrc_send_data_message(ctx, h, u, k, r, p);
            Marshal.FreeHGlobal(h); Marshal.FreeHGlobal(u); Marshal.FreeHGlobal(k); Marshal.FreeHGlobal(r); Marshal.FreeHGlobal(p);
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

        public static string? EncryptLocalState(IntPtr ctx, string plaintext, string deviceUuid)
        {
            var p = StringToPtr(plaintext);
            var u = StringToPtr(deviceUuid);
            var result = nrc_encrypt_local_state(ctx, p, u);
            Marshal.FreeHGlobal(p);
            Marshal.FreeHGlobal(u);
            return PtrToStringAndFree(result);
        }

        public static string? DecryptLocalState(IntPtr ctx, string encryptedB64, string deviceUuid)
        {
            var e = StringToPtr(encryptedB64);
            var u = StringToPtr(deviceUuid);
            var result = nrc_decrypt_local_state(ctx, e, u);
            Marshal.FreeHGlobal(e);
            Marshal.FreeHGlobal(u);
            return PtrToStringAndFree(result);
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

        // ======== New safe wrappers ========

        public static int GenerateKeypair(IntPtr ctx)
        {
            return NotifyRelayCore.nrc_ecdh_generate_keypair(ctx);
        }

        public static string? GetPublicKey(IntPtr ctx)
        {
            return PtrToStringAndFree(NotifyRelayCore.nrc_ecdh_get_public_key(ctx));
        }

        public static int HasKeypair(IntPtr ctx)
        {
            return NotifyRelayCore.nrc_ecdh_has_keypair(ctx);
        }

        public static int GenerateEphemeralKeypair(IntPtr ctx)
        {
            return NotifyRelayCore.nrc_ecdh_generate_ephemeral_keypair(ctx);
        }

        public static string? GetEphemeralPublicKey(IntPtr ctx)
        {
            return PtrToStringAndFree(NotifyRelayCore.nrc_ecdh_get_ephemeral_public_key(ctx));
        }

        public static int HasEphemeralKeypair(IntPtr ctx)
        {
            return NotifyRelayCore.nrc_ecdh_has_ephemeral_keypair(ctx);
        }

        public static void ClearEphemeralKeypair(IntPtr ctx)
        {
            NotifyRelayCore.nrc_ecdh_clear_ephemeral_keypair(ctx);
        }

        public static int DerivePairingKey(IntPtr ctx, string peerEphPubB64)
        {
            var p = StringToPtr(peerEphPubB64);
            var result = NotifyRelayCore.nrc_ecdh_derive_pairing_key(ctx, p);
            Marshal.FreeHGlobal(p);
            return result;
        }

        public static string? EncryptPairingCode(IntPtr ctx, string code)
        {
            var c = StringToPtr(code);
            var result = NotifyRelayCore.nrc_ecdh_encrypt_pairing_code(ctx, c);
            Marshal.FreeHGlobal(c);
            return PtrToStringAndFree(result);
        }

        public static string? DecryptPairingCode(IntPtr ctx, string encryptedB64)
        {
            var e = StringToPtr(encryptedB64);
            var result = NotifyRelayCore.nrc_ecdh_decrypt_pairing_code(ctx, e);
            Marshal.FreeHGlobal(e);
            return PtrToStringAndFree(result);
        }

        public static int DeriveLongTermKey(IntPtr ctx, string peerUuid, string peerLtPubB64)
        {
            var u = StringToPtr(peerUuid);
            var k = StringToPtr(peerLtPubB64);
            var result = NotifyRelayCore.nrc_ecdh_derive_long_term_key(ctx, u, k);
            Marshal.FreeHGlobal(u);
            Marshal.FreeHGlobal(k);
            return result;
        }

        public static string? ExportDeviceKey(IntPtr ctx, string deviceUuid)
        {
            var u = StringToPtr(deviceUuid);
            var result = NotifyRelayCore.nrc_export_device_key(ctx, u);
            Marshal.FreeHGlobal(u);
            return PtrToStringAndFree(result);
        }

        public static string? ExportLocalKeypair(IntPtr ctx)
        {
            return PtrToStringAndFree(NotifyRelayCore.nrc_export_local_keypair(ctx));
        }

        public static int ProcessLine(IntPtr ctx, string line)
        {
            var l = StringToPtr(line);
            var result = NotifyRelayCore.nrc_process_line(ctx, l);
            Marshal.FreeHGlobal(l);
            return result;
        }

        public static void SetUserData(IntPtr ctx, IntPtr userData)
        {
            NotifyRelayCore.nrc_set_user_data(ctx, userData);
        }

        // ======== New safe wrappers ========

        public static int VerifyPairingCode(string storedCode, string inputCode)
        {
            var s = StringToPtr(storedCode);
            var i = StringToPtr(inputCode);
            var result = NotifyRelayCore.nrc_verify_pairing_code(s, i);
            Marshal.FreeHGlobal(s);
            Marshal.FreeHGlobal(i);
            return result;
        }

        public static string? ComputeDedupKey(string deviceUuid, string data)
        {
            var u = StringToPtr(deviceUuid);
            var d = StringToPtr(data);
            var result = NotifyRelayCore.nrc_compute_dedup_key(u, d);
            Marshal.FreeHGlobal(u);
            Marshal.FreeHGlobal(d);
            return PtrToStringAndFree(result);
        }

        public static int HeartbeatHasTimedOut(long lastHeartbeatSec, long nowSec, long timeoutSec)
        {
            return NotifyRelayCore.nrc_heartbeat_has_timed_out(lastHeartbeatSec, nowSec, timeoutSec);
        }

        public static string? ComputeFeatureId(string packageName, string title, string text)
        {
            var p = StringToPtr(packageName);
            var t = StringToPtr(title);
            var tx = StringToPtr(text);
            var result = NotifyRelayCore.nrc_compute_feature_id(p, t, tx);
            Marshal.FreeHGlobal(p);
            Marshal.FreeHGlobal(t);
            Marshal.FreeHGlobal(tx);
            return PtrToStringAndFree(result);
        }

        public static int ParseHeartbeatWithCb(string line, OnHeartbeatWithCb cb, IntPtr userData)
        {
            var l = StringToPtr(line);
            var result = NotifyRelayCore.nrc_parse_heartbeat_with_cb(l, cb, userData);
            Marshal.FreeHGlobal(l);
            return result;
        }

        public static int ParseHeartbeatTcpWithCb(string line, OnHeartbeatTcpWithCb cb, IntPtr userData)
        {
            var l = StringToPtr(line);
            var result = NotifyRelayCore.nrc_parse_heartbeat_tcp_with_cb(l, cb, userData);
            Marshal.FreeHGlobal(l);
            return result;
        }

        public static int HeartbeatTick(IntPtr ctx, long timeoutSec)
        {
            return NotifyRelayCore.nrc_heartbeat_tick(ctx, timeoutSec);
        }

        // ======== Network layer safe wrappers ========

        public static int StartTcpServer(IntPtr ctx, ushort port)
        {
            return NotifyRelayCore.nrc_start_tcp_server(ctx, port);
        }

        public static int StopTcpServer(IntPtr ctx)
        {
            return NotifyRelayCore.nrc_stop_tcp_server(ctx);
        }

        public static int SendToDevice(IntPtr ctx, string uuid, string message)
        {
            var u = StringToPtr(uuid);
            var m = StringToPtr(message);
            var result = NotifyRelayCore.nrc_send_to_device(ctx, u, m);
            Marshal.FreeHGlobal(u);
            Marshal.FreeHGlobal(m);
            return result;
        }

        public static int BroadcastMessage(IntPtr ctx, string message)
        {
            var m = StringToPtr(message);
            var result = NotifyRelayCore.nrc_broadcast_message(ctx, m);
            Marshal.FreeHGlobal(m);
            return result;
        }

        public static int GetConnectedDeviceCount(IntPtr ctx)
        {
            return NotifyRelayCore.nrc_get_connected_device_count(ctx);
        }

        public static int IsDeviceConnected(IntPtr ctx, string uuid)
        {
            var u = StringToPtr(uuid);
            var result = NotifyRelayCore.nrc_is_device_connected(ctx, u);
            Marshal.FreeHGlobal(u);
            return result;
        }

        public static int RemoveDeviceSession(IntPtr ctx, string uuid)
        {
            var u = StringToPtr(uuid);
            var result = NotifyRelayCore.nrc_remove_device_session(ctx, u);
            Marshal.FreeHGlobal(u);
            return result;
        }
    }
}
