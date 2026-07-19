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
    public static extern IntPtr nrc_get_git_hash();

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
    public static extern int nrc_process_line(IntPtr ctx, IntPtr line);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern void nrc_set_user_data(IntPtr ctx, IntPtr userData);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern int nrc_periodic_broadcast(IntPtr ctx, int action, IntPtr uuid, IntPtr name, int battery, IntPtr deviceType);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr nrc_export_state(IntPtr ctx);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern int nrc_import_state(IntPtr ctx, IntPtr json);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr nrc_encrypt_local_state(IntPtr ctx, IntPtr plaintext, IntPtr deviceUuid);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr nrc_decrypt_local_state(IntPtr ctx, IntPtr encryptedB64, IntPtr deviceUuid);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr nrc_export_device_key(IntPtr ctx, IntPtr deviceUuid);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern void nrc_free_string(IntPtr s);

    // ======== Updated Send functions ========
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

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern int nrc_validate_pairing_code(IntPtr ctx, IntPtr code);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern void nrc_send_heartbeat_udp(IntPtr ctx, IntPtr uuid, IntPtr name, ushort port, int battery, IntPtr deviceType);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern void nrc_send_discovery(IntPtr ctx, IntPtr uuid, IntPtr name, ushort port, int battery, IntPtr deviceType);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern void nrc_send_data_message(IntPtr ctx, IntPtr header, IntPtr localUuid, IntPtr localPubKey, IntPtr remoteUuid, IntPtr plaintext);

    // ======== New: OneShot TCP (ctx param, unified timeout) ========
    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern int nrc_oneshot_send_receive(IntPtr ctx, IntPtr ip, ushort port, IntPtr payload, uint timeoutMs);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern int nrc_oneshot_send_only(IntPtr ctx, IntPtr ip, ushort port, IntPtr payload, uint timeoutMs);

    // ======== Network layer ========
    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern int nrc_start_tcp_server(IntPtr ctx, ushort port);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern int nrc_stop_tcp_server(IntPtr ctx);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern int nrc_restart_udp_listener(IntPtr ctx);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern int nrc_broadcast_message(IntPtr ctx, IntPtr message);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern int nrc_get_connected_device_count(IntPtr ctx);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern int nrc_is_device_connected(IntPtr ctx, IntPtr uuid);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern int nrc_remove_device_session(IntPtr ctx, IntPtr uuid);

    // ======== Heartbeat sender ========
    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern long nrc_start_heartbeat_sender(IntPtr ctx, IntPtr uuid, IntPtr name, int battery, IntPtr deviceType, IntPtr ip, ulong intervalMs, int mode);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern void nrc_update_heartbeat_params(IntPtr ctx, long handlePtr, IntPtr uuid, IntPtr name, int battery, IntPtr deviceType);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern void nrc_stop_heartbeat_sender(IntPtr ctx, long handlePtr);

    // ======== Offline detector ========
    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern long nrc_start_offline_detector(IntPtr ctx, long timeoutSec, ulong checkIntervalMs);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern void nrc_stop_offline_detector(IntPtr ctx);

    // ======== Sender queue ========
    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern long nrc_create_sender_queue(IntPtr ctx);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern void nrc_start_sender_queue(IntPtr ctx, long queuePtr);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern void nrc_enqueue_message(IntPtr ctx, long queuePtr, IntPtr deviceUuid, IntPtr header, IntPtr plaintext, IntPtr dedupKey);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern void nrc_stop_sender_queue(IntPtr ctx, long queuePtr);

    // ======== Diff ========
    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr nrc_compute_superisland_diff(IntPtr oldState, IntPtr newState);

    // ======== Network change ========
    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern void nrc_on_network_changed(IntPtr ctx, IntPtr localIp);

    // ======== Local IP ========
    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr nrc_get_local_ip();

    // ======== Discovery ========
    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern void nrc_add_known_device(IntPtr ctx, IntPtr uuid, IntPtr ip);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern void nrc_remove_known_device(IntPtr ctx, IntPtr uuid);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern void nrc_record_discovered_device(IntPtr ctx, IntPtr uuid, IntPtr name, IntPtr ip, ushort port, int battery, IntPtr deviceType);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr nrc_get_discovered_devices(IntPtr ctx);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern void nrc_start_known_device_scanner(IntPtr ctx);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern void nrc_stop_known_device_scanner(IntPtr ctx);

    // ======== Filter ========
    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern int nrc_set_filter_config(IntPtr ctx, IntPtr configJson);
    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr nrc_map_local_package(IntPtr ctx, IntPtr pkg);
    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern int nrc_check_filter_mode(IntPtr ctx, IntPtr mappedPkg, IntPtr originalPkg, IntPtr title, IntPtr text);
    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern int nrc_filter_notification(IntPtr ctx, IntPtr pkg, IntPtr title, IntPtr text);

    // ======== Other utilities ========
    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr nrc_compute_dedup_key(IntPtr deviceUuid, IntPtr data);
    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr nrc_compute_feature_id(IntPtr superPkg, IntPtr paramV2Raw, IntPtr title, IntPtr text, IntPtr instanceId);
    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr nrc_compute_feature_id_simple(IntPtr packageName, IntPtr title, IntPtr text);
    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern double nrc_text_similarity(IntPtr a, IntPtr b);
    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern int nrc_should_deduplicate(IntPtr newTitle, IntPtr newText, IntPtr oldTitle, IntPtr oldText);

    // ======== FTP credentials ========
    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr nrc_derive_ftp_credentials(IntPtr sharedSecretB64);
    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr nrc_derive_password_hash(IntPtr password);
    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr nrc_generate_random_password();

    // ======== Dedup unified ========
    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern int nrc_dedup(IntPtr ctx, int action, IntPtr dedupKey, long arg1Ms, long arg2Ms);

    // ======== Callback delegate types ========
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public delegate void OnPairingCb(IntPtr uuid, IntPtr messageType, IntPtr data, int intValue, IntPtr extra, IntPtr userData);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public delegate void OnDataCb(IntPtr uuid, IntPtr messageType, IntPtr plaintext, IntPtr userData);

    [UnmanagedFunctionPointer(CallingConvention = CallingConvention.Cdecl)]
    public delegate void OnLogCb(int level, IntPtr message);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public delegate void OnHeartbeatUdpCb(IntPtr uuid, IntPtr nameB64, ushort port, int battery, IntPtr deviceType, IntPtr userData);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public delegate void OnDeviceTimeoutCb(IntPtr uuid, IntPtr userData);

    // ======== Network callbacks ========
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public delegate void OnDeviceConnectedCb(IntPtr uuid, IntPtr ip, IntPtr userData);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public delegate void OnDeviceDisconnectedCb(IntPtr uuid, IntPtr userData);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public delegate void OnTcpErrorCb(IntPtr error, IntPtr userData);

    // ======== Callback setters ========
    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern void nrc_set_log_callback(IntPtr cb);
    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern void nrc_set_on_pairing_cb(IntPtr ctx, OnPairingCb cb);
    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern void nrc_set_on_data_cb(IntPtr ctx, OnDataCb cb);
    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern void nrc_set_on_heartbeat_udp_cb(IntPtr ctx, OnHeartbeatUdpCb cb);
    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern void nrc_set_on_device_timeout_cb(IntPtr ctx, OnDeviceTimeoutCb cb);
    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern void nrc_set_on_device_connected_cb(IntPtr ctx, OnDeviceConnectedCb cb);
    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern void nrc_set_on_device_disconnected_cb(IntPtr ctx, OnDeviceDisconnectedCb cb);
    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern void nrc_set_on_tcp_error_cb(IntPtr ctx, OnTcpErrorCb cb);

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
        // ======== Key management ========
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
            var h = StringToPtr(header); var u = StringToPtr(localUuid); var k = StringToPtr(localPubKey);
            var r = StringToPtr(remoteUuid); var p = StringToPtr(plaintext);
            var result = NotifyRelayCore.nrc_encrypt_message(ctx, h, u, k, r, p);
            Marshal.FreeHGlobal(h); Marshal.FreeHGlobal(u); Marshal.FreeHGlobal(k); Marshal.FreeHGlobal(r); Marshal.FreeHGlobal(p);
            return PtrToStringAndFree(result);
        }

        public static int DeriveSharedSecret(IntPtr ctx, string peerUuid, string peerPubKeyB64)
        {
            var u = StringToPtr(peerUuid); var k = StringToPtr(peerPubKeyB64);
            var result = NotifyRelayCore.nrc_ecdh_derive_shared_secret(ctx, u, k);
            Marshal.FreeHGlobal(u); Marshal.FreeHGlobal(k);
            return result;
        }

        public static int PeriodicBroadcast(IntPtr ctx, int action, string? uuid, string? name, int battery, string? deviceType)
        {
            var u = StringToPtr(uuid); var n = StringToPtr(name); var d = StringToPtr(deviceType);
            var result = NotifyRelayCore.nrc_periodic_broadcast(ctx, action, u, n, battery, d);
            Marshal.FreeHGlobal(u); Marshal.FreeHGlobal(n); Marshal.FreeHGlobal(d);
            return result;
        }

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

        // ======== Pairing code management (Rust-generated) ========
        public static string? GeneratePairingCode(IntPtr ctx, uint ttlSecs = 300)
        {
            return PtrToStringAndFree(NotifyRelayCore.nrc_generate_pairing_code(ctx, ttlSecs));
        }

        public static void ClearPairingCode(IntPtr ctx)
        {
            NotifyRelayCore.nrc_clear_pairing_code(ctx);
        }

        public static int ValidatePairingCode(IntPtr ctx, string code)
        {
            var c = StringToPtr(code);
            var result = NotifyRelayCore.nrc_validate_pairing_code(ctx, c);
            Marshal.FreeHGlobal(c);
            return result;
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
            var h = StringToPtr(header); var u = StringToPtr(localUuid); var k = StringToPtr(localPubKey);
            var r = StringToPtr(remoteUuid); var p = StringToPtr(plaintext);
            NotifyRelayCore.nrc_send_data_message(ctx, h, u, k, r, p);
            Marshal.FreeHGlobal(h); Marshal.FreeHGlobal(u); Marshal.FreeHGlobal(k); Marshal.FreeHGlobal(r); Marshal.FreeHGlobal(p);
        }

        // ======== OneShot TCP ========
        public static int OneShotSendReceive(IntPtr ctx, string ip, ushort port, string payload, uint timeoutMs = 5000)
        {
            var i = StringToPtr(ip); var p = StringToPtr(payload);
            var result = NotifyRelayCore.nrc_oneshot_send_receive(ctx, i, port, p, timeoutMs);
            Marshal.FreeHGlobal(i); Marshal.FreeHGlobal(p);
            return result;
        }

        public static int OneShotSendOnly(IntPtr ctx, string ip, ushort port, string payload, uint timeoutMs = 5000)
        {
            var i = StringToPtr(ip); var p = StringToPtr(payload);
            var result = NotifyRelayCore.nrc_oneshot_send_only(ctx, i, port, p, timeoutMs);
            Marshal.FreeHGlobal(i); Marshal.FreeHGlobal(p);
            return result;
        }

        // ======== State persistence ========
        public static string? ExportState(IntPtr ctx)
        {
            return PtrToStringAndFree(nrc_export_state(ctx));
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
            var p = StringToPtr(plaintext); var u = StringToPtr(deviceUuid);
            var result = nrc_encrypt_local_state(ctx, p, u);
            Marshal.FreeHGlobal(p); Marshal.FreeHGlobal(u);
            return PtrToStringAndFree(result);
        }

        public static string? DecryptLocalState(IntPtr ctx, string encryptedB64, string deviceUuid)
        {
            var e = StringToPtr(encryptedB64); var u = StringToPtr(deviceUuid);
            var result = nrc_decrypt_local_state(ctx, e, u);
            Marshal.FreeHGlobal(e); Marshal.FreeHGlobal(u);
            return PtrToStringAndFree(result);
        }

        public static string? ExportDeviceKey(IntPtr ctx, string deviceUuid)
        {
            var u = StringToPtr(deviceUuid);
            var result = NotifyRelayCore.nrc_export_device_key(ctx, u);
            Marshal.FreeHGlobal(u);
            return PtrToStringAndFree(result);
        }

        // ======== Key management wrappers ========
        public static int GenerateKeypair(IntPtr ctx) => NotifyRelayCore.nrc_ecdh_generate_keypair(ctx);
        public static string? GetPublicKey(IntPtr ctx) => PtrToStringAndFree(NotifyRelayCore.nrc_ecdh_get_public_key(ctx));
        public static int HasKeypair(IntPtr ctx) => NotifyRelayCore.nrc_ecdh_has_keypair(ctx);

        // ======== Process ========
        public static int ProcessLine(IntPtr ctx, string line)
        {
            var l = StringToPtr(line);
            var result = NotifyRelayCore.nrc_process_line(ctx, l);
            Marshal.FreeHGlobal(l);
            return result;
        }

        public static void SetUserData(IntPtr ctx, IntPtr userData) => NotifyRelayCore.nrc_set_user_data(ctx, userData);

        // ======== Dedup unified ========
        public static int Dedup(IntPtr ctx, int action, string dedupKey, long arg1Ms, long arg2Ms)
        {
            var k = StringToPtr(dedupKey);
            var result = NotifyRelayCore.nrc_dedup(ctx, action, k, arg1Ms, arg2Ms);
            Marshal.FreeHGlobal(k);
            return result;
        }

        // ======== Filter ========
        public static int SetFilterConfig(IntPtr ctx, string configJson)
        {
            var json = StringToPtr(configJson);
            var result = NotifyRelayCore.nrc_set_filter_config(ctx, json);
            Marshal.FreeHGlobal(json);
            return result;
        }

        public static string? MapLocalPackage(IntPtr ctx, string pkg)
        {
            var p = StringToPtr(pkg);
            var result = NotifyRelayCore.nrc_map_local_package(ctx, p);
            Marshal.FreeHGlobal(p);
            return PtrToStringAndFree(result);
        }

        public static int CheckFilterMode(IntPtr ctx, string mappedPkg, string originalPkg, string title, string text)
        {
            var mp = StringToPtr(mappedPkg); var op = StringToPtr(originalPkg); var t = StringToPtr(title); var tx = StringToPtr(text);
            var result = NotifyRelayCore.nrc_check_filter_mode(ctx, mp, op, t, tx);
            Marshal.FreeHGlobal(mp); Marshal.FreeHGlobal(op); Marshal.FreeHGlobal(t); Marshal.FreeHGlobal(tx);
            return result;
        }

        // ======== Utility wrappers ========
        public static string? ComputeDedupKey(string deviceUuid, string data)
        {
            var u = StringToPtr(deviceUuid); var d = StringToPtr(data);
            var result = NotifyRelayCore.nrc_compute_dedup_key(u, d);
            Marshal.FreeHGlobal(u); Marshal.FreeHGlobal(d);
            return PtrToStringAndFree(result);
        }

        public static string? ComputeFeatureId(string superPkg, string paramV2Raw, string title, string text, string instanceId)
        {
            var pkg = StringToPtr(superPkg); var param = StringToPtr(paramV2Raw); var t = StringToPtr(title);
            var tx = StringToPtr(text); var iid = StringToPtr(instanceId);
            var result = NotifyRelayCore.nrc_compute_feature_id(pkg, param, t, tx, iid);
            Marshal.FreeHGlobal(pkg); Marshal.FreeHGlobal(param); Marshal.FreeHGlobal(t); Marshal.FreeHGlobal(tx); Marshal.FreeHGlobal(iid);
            return PtrToStringAndFree(result);
        }

        public static string? ComputeFeatureIdSimple(string packageName, string title, string text)
        {
            var p = StringToPtr(packageName); var t = StringToPtr(title); var tx = StringToPtr(text);
            var result = NotifyRelayCore.nrc_compute_feature_id_simple(p, t, tx);
            Marshal.FreeHGlobal(p); Marshal.FreeHGlobal(t); Marshal.FreeHGlobal(tx);
            return PtrToStringAndFree(result);
        }

        public static double TextSimilarity(string a, string b)
        {
            var aPtr = StringToPtr(a); var bPtr = StringToPtr(b);
            var result = NotifyRelayCore.nrc_text_similarity(aPtr, bPtr);
            Marshal.FreeHGlobal(aPtr); Marshal.FreeHGlobal(bPtr);
            return result;
        }

        public static int ShouldDeduplicate(string newTitle, string newText, string oldTitle, string oldText)
        {
            var nt = StringToPtr(newTitle); var ntx = StringToPtr(newText); var ot = StringToPtr(oldTitle); var otx = StringToPtr(oldText);
            var result = NotifyRelayCore.nrc_should_deduplicate(nt, ntx, ot, otx);
            Marshal.FreeHGlobal(nt); Marshal.FreeHGlobal(ntx); Marshal.FreeHGlobal(ot); Marshal.FreeHGlobal(otx);
            return result;
        }

        public static bool FilterNotification(IntPtr ctx, string pkg, string title, string text)
        {
            var p = StringToPtr(pkg); var t = StringToPtr(title); var tx = StringToPtr(text);
            var result = NotifyRelayCore.nrc_filter_notification(ctx, p, t, tx);
            Marshal.FreeHGlobal(p); Marshal.FreeHGlobal(t); Marshal.FreeHGlobal(tx);
            return result != 0;
        }

        // ======== FTP ========
        public static string? DeriveFtpCredentials(string sharedSecretB64)
        {
            var s = StringToPtr(sharedSecretB64);
            var result = NotifyRelayCore.nrc_derive_ftp_credentials(s);
            Marshal.FreeHGlobal(s);
            return PtrToStringAndFree(result);
        }

        public static string? DerivePasswordHash(string password)
        {
            var p = StringToPtr(password);
            var result = NotifyRelayCore.nrc_derive_password_hash(p);
            Marshal.FreeHGlobal(p);
            return PtrToStringAndFree(result);
        }

        public static string? GenerateRandomPassword()
        {
            var result = NotifyRelayCore.nrc_generate_random_password();
            return PtrToStringAndFree(result);
        }

        // ======== Network wrappers ========
        public static int StartTcpServer(IntPtr ctx, ushort port) => NotifyRelayCore.nrc_start_tcp_server(ctx, port);
        public static int StopTcpServer(IntPtr ctx) => NotifyRelayCore.nrc_stop_tcp_server(ctx);
        public static int RestartUdpListener(IntPtr ctx) => NotifyRelayCore.nrc_restart_udp_listener(ctx);

        public static int BroadcastMessage(IntPtr ctx, string message)
        {
            var m = StringToPtr(message);
            var result = NotifyRelayCore.nrc_broadcast_message(ctx, m);
            Marshal.FreeHGlobal(m);
            return result;
        }

        public static int GetConnectedDeviceCount(IntPtr ctx) => NotifyRelayCore.nrc_get_connected_device_count(ctx);
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

        // ======== Heartbeat sender ========
        public static long StartHeartbeatSender(IntPtr ctx, string uuid, string name, int battery, string deviceType, string ip, ulong intervalMs, int mode)
        {
            var u = StringToPtr(uuid); var n = StringToPtr(name); var d = StringToPtr(deviceType); var i = StringToPtr(ip);
            var result = NotifyRelayCore.nrc_start_heartbeat_sender(ctx, u, n, battery, d, i, intervalMs, mode);
            Marshal.FreeHGlobal(u); Marshal.FreeHGlobal(n); Marshal.FreeHGlobal(d); Marshal.FreeHGlobal(i);
            return result;
        }

        public static void UpdateHeartbeatParams(IntPtr ctx, long handlePtr, string uuid, string name, int battery, string deviceType)
        {
            var u = StringToPtr(uuid); var n = StringToPtr(name); var d = StringToPtr(deviceType);
            NotifyRelayCore.nrc_update_heartbeat_params(ctx, handlePtr, u, n, battery, d);
            Marshal.FreeHGlobal(u); Marshal.FreeHGlobal(n); Marshal.FreeHGlobal(d);
        }

        // ======== Offline detector ========
        public static long StartOfflineDetector(IntPtr ctx, long timeoutSec, ulong checkIntervalMs)
        {
            return NotifyRelayCore.nrc_start_offline_detector(ctx, timeoutSec, checkIntervalMs);
        }

        // ======== Sender queue ========
        public static long CreateSenderQueue(IntPtr ctx)
        {
            return NotifyRelayCore.nrc_create_sender_queue(ctx);
        }

        public static void StartSenderQueue(IntPtr ctx, long queuePtr)
        {
            NotifyRelayCore.nrc_start_sender_queue(ctx, queuePtr);
        }

        public static void EnqueueMessage(IntPtr ctx, long queuePtr, string deviceUuid, string header, string plaintext, string? dedupKey)
        {
            var u = StringToPtr(deviceUuid);
            var h = StringToPtr(header); var p = StringToPtr(plaintext);
            var d = StringToPtr(dedupKey);
            NotifyRelayCore.nrc_enqueue_message(ctx, queuePtr, u, h, p, d);
            Marshal.FreeHGlobal(u); Marshal.FreeHGlobal(h); Marshal.FreeHGlobal(p);
            if (d != IntPtr.Zero) Marshal.FreeHGlobal(d);
        }

        // ======== Discovery ========
        public static void AddKnownDevice(IntPtr ctx, string uuid, string ip)
        {
            var u = StringToPtr(uuid); var i = StringToPtr(ip);
            NotifyRelayCore.nrc_add_known_device(ctx, u, i);
            Marshal.FreeHGlobal(u); Marshal.FreeHGlobal(i);
        }

        public static void RemoveKnownDevice(IntPtr ctx, string uuid)
        {
            var u = StringToPtr(uuid);
            NotifyRelayCore.nrc_remove_known_device(ctx, u);
            Marshal.FreeHGlobal(u);
        }

        public static void RecordDiscoveredDevice(IntPtr ctx, string uuid, string? name, string ip, ushort port, int battery, string deviceType)
        {
            var u = StringToPtr(uuid); var n = StringToPtr(name);
            var i = StringToPtr(ip); var d = StringToPtr(deviceType);
            NotifyRelayCore.nrc_record_discovered_device(ctx, u, n, i, port, battery, d);
            Marshal.FreeHGlobal(u); Marshal.FreeHGlobal(n); Marshal.FreeHGlobal(i); Marshal.FreeHGlobal(d);
        }

        // ======== Diff ========
        public static string? ComputeSuperIslandDiff(string oldState, string newState)
        {
            var o = StringToPtr(oldState); var n = StringToPtr(newState);
            var result = NotifyRelayCore.nrc_compute_superisland_diff(o, n);
            Marshal.FreeHGlobal(o); Marshal.FreeHGlobal(n);
            return PtrToStringAndFree(result);
        }
    }
}
