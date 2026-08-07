using System.Runtime.InteropServices;
using System.Text;

namespace NotifyRelay.Native;

public static class NotifyRelayCore
{
    private const string DllName = "notify_relay_core";

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr nrc_init();

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

    // ======== Network layer ========
    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern int nrc_remove_device_session(IntPtr ctx, IntPtr uuid);

    // ======== Core start (统一启动 TCP/UDP、心跳、离线检测、发送队列、扫描、重连、mDNS) ========
    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern long nrc_start_core(IntPtr ctx, IntPtr uuid, IntPtr name, int battery, IntPtr deviceType, ushort tcpPort, IntPtr pubKey, ulong heartbeatIntervalMs, long offlineTimeoutSec, ulong offlineCheckIntervalMs, ulong reconnectIntervalSecs, uint reconnectMaxRetries);

    // ======== Heartbeat scheduler params (电量/名称变化) ========
    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern void nrc_update_heartbeat_scheduler_params(IntPtr ctx, IntPtr name, int battery, IntPtr deviceType);

    // ======== Device state snapshot ========
    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr nrc_get_device_list(IntPtr ctx, long authedTimeoutMs, long unauthedTimeoutMs);

    // ======== Sender queue ========
    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern void nrc_enqueue_message(IntPtr ctx, long queuePtr, IntPtr deviceUuid, IntPtr header, IntPtr plaintext, IntPtr dedupKey);

    // ======== Clipboard ========
    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr nrc_clipboard_on_changed(IntPtr ctx, long queuePtr, IntPtr targetsJson, IntPtr mime, IntPtr content, long nowMs, int force);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr nrc_clipboard_on_received(IntPtr ctx, IntPtr payloadJson, long nowMs);

    // ======== App sync (app list & icons) ========
    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr nrc_app_sync_prepare_icon_request(IntPtr ctx, IntPtr packagesJson, IntPtr installedJson, IntPtr cachedJson, IntPtr appDeviceJson, IntPtr sourceDeviceUuid, long nowMs);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern void nrc_app_sync_clear_icon_pending(IntPtr ctx, IntPtr packagesJson);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr nrc_app_sync_parse_icon_response(IntPtr payloadJson);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr nrc_app_sync_build_applist_request(IntPtr scope, long nowMs);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr nrc_app_sync_parse_applist_response(IntPtr payloadJson);

    // ======== State merge (push full; receive via on_data) ========
    // isQuery: 1=查询回调响应推送（心跳查询发现变更后由平台推送），0=正常主动推送
    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern int nrc_push_superisland_state(IntPtr ctx, IntPtr queuePtr, IntPtr deviceUuid, IntPtr fullJson, int isEnd, int isQuery);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern int nrc_push_media_state(IntPtr ctx, IntPtr queuePtr, IntPtr deviceUuid, IntPtr fullJson, int isEnd, int isQuery);

    // ======== Network change ========
    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern void nrc_on_network_changed(IntPtr ctx, IntPtr localIp);

    // ======== Local IP ========
    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr nrc_get_local_ip();

    // ======== mDNS ========
    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern void nrc_stop_mdns_advertiser(IntPtr ctx);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern void nrc_stop_mdns_discovery(IntPtr ctx);

    // ======== Discovery ========
    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern void nrc_add_known_device(IntPtr ctx, IntPtr uuid, IntPtr ip);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern void nrc_remove_known_device(IntPtr ctx, IntPtr uuid);

    // ======== Filter ========
    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern int nrc_set_filter_config(IntPtr ctx, IntPtr configJson);
    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr nrc_map_local_package(IntPtr ctx, IntPtr pkg);
    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern int nrc_check_filter_mode(IntPtr ctx, IntPtr mappedPkg, IntPtr originalPkg, IntPtr title, IntPtr text);

    // ======== Other utilities ========
    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr nrc_compute_dedup_key(IntPtr deviceUuid, IntPtr data);
    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr nrc_compute_feature_id(IntPtr superPkg, IntPtr paramV2Raw, IntPtr title, IntPtr text, IntPtr instanceId);

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

    // 状态查询回调（Rust 心跳线程锁外调用）：返回 0=不存在 / 1=存在无变更 / 2=存在有变更
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public delegate int OnStateQueryCb(IntPtr uuid, IntPtr featureId, int isMedia, IntPtr userData);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public delegate void OnLogCb(int level, IntPtr message);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public delegate void OnHeartbeatUdpCb(IntPtr uuid, IntPtr nameB64, ushort port, int battery, IntPtr deviceType, IntPtr ip, IntPtr userData);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public delegate void OnMdnsDiscoveredCb(IntPtr uuid, IntPtr name, IntPtr ip, ushort port, int battery, IntPtr deviceType, IntPtr userData);

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
    public static extern void nrc_set_on_state_query_cb(IntPtr ctx, OnStateQueryCb cb);
    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern void nrc_set_on_heartbeat_udp_cb(IntPtr ctx, OnHeartbeatUdpCb cb);
    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern void nrc_set_on_mdns_discovered_cb(IntPtr ctx, OnMdnsDiscoveredCb cb);
    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern void nrc_set_on_device_timeout_cb(IntPtr ctx, OnDeviceTimeoutCb cb);
    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern void nrc_set_on_device_connected_cb(IntPtr ctx, OnDeviceConnectedCb cb);
    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern void nrc_set_on_device_disconnected_cb(IntPtr ctx, OnDeviceDisconnectedCb cb);
    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern void nrc_set_on_tcp_error_cb(IntPtr ctx, OnTcpErrorCb cb);

    // ======== Audio stream ========
    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern int nrc_audio_start(IntPtr ctx, IntPtr direction, int port, int sampleRate, int channels, IntPtr remoteUuid);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern int nrc_audio_write_frame(IntPtr ctx, [In] byte[] pcmData, int pcmLen);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern int nrc_audio_stop(IntPtr ctx);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern int nrc_audio_is_active(IntPtr ctx);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern void nrc_register_audio_data_cb(IntPtr ctx, AudioDataCallback cb);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern void nrc_register_audio_event_cb(IntPtr ctx, AudioEventCallback cb);

    // ======== Audio callback delegate types ========
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public delegate void AudioDataCallback(IntPtr deviceUuid, IntPtr pcmData, int pcmLen, int sampleRate, int channels, IntPtr userData);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public delegate void AudioEventCallback(IntPtr deviceUuid, IntPtr eventStr, IntPtr errorMsg, IntPtr userData);

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
        public static long StartCore(IntPtr ctx, string uuid, string name, int battery, string deviceType, ushort tcpPort, string pubKey, ulong heartbeatIntervalMs, long offlineTimeoutSec, ulong offlineCheckIntervalMs, ulong reconnectIntervalSecs, uint reconnectMaxRetries)
        {
            var u = StringToPtr(uuid); var n = StringToPtr(name); var d = StringToPtr(deviceType); var pk = StringToPtr(pubKey);
            var result = NotifyRelayCore.nrc_start_core(ctx, u, n, battery, d, tcpPort, pk, heartbeatIntervalMs, offlineTimeoutSec, offlineCheckIntervalMs, reconnectIntervalSecs, reconnectMaxRetries);
            Marshal.FreeHGlobal(u); Marshal.FreeHGlobal(n); Marshal.FreeHGlobal(d); Marshal.FreeHGlobal(pk);
            return result;
        }

        public static int RemoveDeviceSession(IntPtr ctx, string uuid)
        {
            var u = StringToPtr(uuid);
            var result = NotifyRelayCore.nrc_remove_device_session(ctx, u);
            Marshal.FreeHGlobal(u);
            return result;
        }

        // ======== Heartbeat scheduler params ========
        public static void UpdateHeartbeatSchedulerParams(IntPtr ctx, string name, int battery, string deviceType)
        {
            var n = StringToPtr(name); var d = StringToPtr(deviceType);
            NotifyRelayCore.nrc_update_heartbeat_scheduler_params(ctx, n, battery, d);
            Marshal.FreeHGlobal(n); Marshal.FreeHGlobal(d);
        }

        public static string? GetDeviceList(IntPtr ctx, long authedTimeoutMs, long unauthedTimeoutMs)
        {
            return PtrToStringAndFree(NotifyRelayCore.nrc_get_device_list(ctx, authedTimeoutMs, unauthedTimeoutMs));
        }

        // ======== Sender queue ========
        public static void EnqueueMessage(IntPtr ctx, long queuePtr, string deviceUuid, string header, string plaintext, string? dedupKey)
        {
            var u = StringToPtr(deviceUuid);
            var h = StringToPtr(header); var p = StringToPtr(plaintext);
            var d = StringToPtr(dedupKey);
            NotifyRelayCore.nrc_enqueue_message(ctx, queuePtr, u, h, p, d);
            Marshal.FreeHGlobal(u); Marshal.FreeHGlobal(h); Marshal.FreeHGlobal(p);
            if (d != IntPtr.Zero) Marshal.FreeHGlobal(d);
        }

        // 推送「全量」超级岛/媒体状态给某设备；Rust 内部做 diff，合并后的全量经 on_data 回调回传。
        // queuePtr 为 SenderQueue 句柄（与 EnqueueMessage 共用同一队列）。
        // isQuery: true=查询回调响应推送（心跳查询发现变更后由平台推送），false=正常主动推送。
        public static int PushSuperIslandState(IntPtr ctx, long queuePtr, string deviceUuid, string fullJson, bool isEnd, bool isQuery = false)
        {
            var u = StringToPtr(deviceUuid); var p = StringToPtr(fullJson);
            var result = NotifyRelayCore.nrc_push_superisland_state(ctx, new IntPtr(queuePtr), u, p, isEnd ? 1 : 0, isQuery ? 1 : 0);
            Marshal.FreeHGlobal(u); Marshal.FreeHGlobal(p);
            return result;
        }

        public static int PushMediaState(IntPtr ctx, long queuePtr, string deviceUuid, string fullJson, bool isEnd, bool isQuery = false)
        {
            var u = StringToPtr(deviceUuid); var p = StringToPtr(fullJson);
            var result = NotifyRelayCore.nrc_push_media_state(ctx, new IntPtr(queuePtr), u, p, isEnd ? 1 : 0, isQuery ? 1 : 0);
            Marshal.FreeHGlobal(u); Marshal.FreeHGlobal(p);
            return result;
        }

        // ======== Clipboard ========
        public static string? ClipboardOnChanged(IntPtr ctx, long queuePtr, string targetsJson, string mime, string content, long nowMs, bool force)
        {
            var t = StringToPtr(targetsJson); var m = StringToPtr(mime); var c = StringToPtr(content);
            var result = NotifyRelayCore.nrc_clipboard_on_changed(ctx, queuePtr, t, m, c, nowMs, force ? 1 : 0);
            Marshal.FreeHGlobal(t); Marshal.FreeHGlobal(m); Marshal.FreeHGlobal(c);
            return PtrToStringAndFree(result);
        }

        public static string? ClipboardOnReceived(IntPtr ctx, string payloadJson, long nowMs)
        {
            var p = StringToPtr(payloadJson);
            var result = NotifyRelayCore.nrc_clipboard_on_received(ctx, p, nowMs);
            Marshal.FreeHGlobal(p);
            return PtrToStringAndFree(result);
        }

        // ======== App sync (app list & icons) ========
        public static string? AppSyncPrepareIconRequest(IntPtr ctx, string packagesJson, string installedJson, string cachedJson, string appDeviceJson, string sourceDeviceUuid, long nowMs)
        {
            var pk = StringToPtr(packagesJson); var in_ = StringToPtr(installedJson); var ca = StringToPtr(cachedJson);
            var ad = StringToPtr(appDeviceJson); var su = StringToPtr(sourceDeviceUuid);
            var result = NotifyRelayCore.nrc_app_sync_prepare_icon_request(ctx, pk, in_, ca, ad, su, nowMs);
            Marshal.FreeHGlobal(pk); Marshal.FreeHGlobal(in_); Marshal.FreeHGlobal(ca); Marshal.FreeHGlobal(ad); Marshal.FreeHGlobal(su);
            return PtrToStringAndFree(result);
        }

        public static void AppSyncClearIconPending(IntPtr ctx, string packagesJson)
        {
            var pk = StringToPtr(packagesJson);
            NotifyRelayCore.nrc_app_sync_clear_icon_pending(ctx, pk);
            Marshal.FreeHGlobal(pk);
        }

        public static string? AppSyncParseIconResponse(string payloadJson)
        {
            var p = StringToPtr(payloadJson);
            var result = NotifyRelayCore.nrc_app_sync_parse_icon_response(p);
            Marshal.FreeHGlobal(p);
            return PtrToStringAndFree(result);
        }

        public static string? AppSyncBuildApplistRequest(string scope, long nowMs)
        {
            var s = StringToPtr(scope);
            var result = NotifyRelayCore.nrc_app_sync_build_applist_request(s, nowMs);
            Marshal.FreeHGlobal(s);
            return PtrToStringAndFree(result);
        }

        public static string? AppSyncParseApplistResponse(string payloadJson)
        {
            var p = StringToPtr(payloadJson);
            var result = NotifyRelayCore.nrc_app_sync_parse_applist_response(p);
            Marshal.FreeHGlobal(p);
            return PtrToStringAndFree(result);
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

        // ======== mDNS ========
        public static void StopMdnsAdvertiser(IntPtr ctx)
        {
            NotifyRelayCore.nrc_stop_mdns_advertiser(ctx);
        }

        public static void StopMdnsDiscovery(IntPtr ctx)
        {
            NotifyRelayCore.nrc_stop_mdns_discovery(ctx);
        }

        // ======== Audio stream ========
        public static int AudioStart(IntPtr ctx, string direction, int port, int sampleRate, int channels, string remoteUuid)
        {
            var d = StringToPtr(direction);
            var u = StringToPtr(remoteUuid);
            var result = NotifyRelayCore.nrc_audio_start(ctx, d, port, sampleRate, channels, u);
            Marshal.FreeHGlobal(d);
            Marshal.FreeHGlobal(u);
            return result;
        }

        public static int AudioWriteFrame(IntPtr ctx, byte[] pcmData, int pcmLen)
        {
            return NotifyRelayCore.nrc_audio_write_frame(ctx, pcmData, pcmLen);
        }

        public static int AudioStop(IntPtr ctx)
        {
            return NotifyRelayCore.nrc_audio_stop(ctx);
        }

        public static int AudioIsActive(IntPtr ctx)
        {
            return NotifyRelayCore.nrc_audio_is_active(ctx);
        }

        public static void RegisterAudioDataCb(IntPtr ctx, AudioDataCallback cb)
        {
            NotifyRelayCore.nrc_register_audio_data_cb(ctx, cb);
        }

        public static void RegisterAudioEventCb(IntPtr ctx, AudioEventCallback cb)
        {
            NotifyRelayCore.nrc_register_audio_event_cb(ctx, cb);
        }
    }
}
