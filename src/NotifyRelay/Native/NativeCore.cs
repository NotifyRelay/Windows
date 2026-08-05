using System.Runtime.InteropServices;
using NotifyRelay.Data.Contracts;
using NotifyRelay.Data.Models;
using NotifyRelay.Services;

namespace NotifyRelay.Native;

public static class NativeCore
{
    private static IntPtr _ctx = IntPtr.Zero;
    private static bool _initialized = false;
    private static string? _gitHash;

    // 回调分发目标
    internal static ProtocolRouter? ProtocolRouter { get; set; }
    internal static IDeviceManager? DeviceManager { get; set; }
    internal static NetworkService? NetworkService { get; set; }
    internal static HeartbeatProcessor? HeartbeatProcessor { get; set; }

    // 保持回调委托不被 GC 回收
    private static readonly List<Delegate> _callbackRefs = new();

    public static IntPtr Context => _ctx;

    public static void Initialize()
    {
        if (_initialized) return;
        _initialized = true;

        var asmLocation = typeof(NotifyRelayCore).Assembly.Location;
        var checkDirs = new[] {
            AppContext.BaseDirectory,
            Path.GetDirectoryName(AppContext.BaseDirectory.TrimEnd('\\')),
            Path.GetDirectoryName(asmLocation)
        };

        foreach (var dir in checkDirs)
        {
            if (dir == null) continue;
            var dllPath = Path.Combine(dir, "notify_relay_core.dll");
            if (File.Exists(dllPath))
            {
                NativeLibrary.Load(dllPath);
                break;
            }
        }

        _ctx = NotifyRelayCore.nrc_init();
        _gitHash = GetGitHash();
    }

    public static void Destroy()
    {
        if (_ctx != IntPtr.Zero)
        {
            NotifyRelayCore.nrc_destroy(_ctx);
            _ctx = IntPtr.Zero;
        }
    }

    public static string? GetGitHash()
    {
        var ptr = NotifyRelayCore.nrc_get_git_hash();
        if (ptr == IntPtr.Zero) return null;
        var result = Marshal.PtrToStringAnsi(ptr);
        NotifyRelayCore.nrc_free_string(ptr);
        return result;
    }

    public static int MigrateSharedSecret(string deviceUuid, byte[] aesKey)
    {
        return NotifyRelayCore.Safe.MigrateSharedSecret(_ctx, deviceUuid, aesKey);
    }

    public static int RemoveDevice(string deviceUuid)
    {
        return NotifyRelayCore.Safe.RemoveDevice(_ctx, deviceUuid);
    }

    public static string? EncryptMessage(string header, string localUuid, string localPubKey, string remoteUuid, string plaintext)
    {
        return NotifyRelayCore.Safe.EncryptMessage(_ctx, header, localUuid, localPubKey, remoteUuid, plaintext);
    }

    // ======== New methods ========

    public static int GenerateKeypair()
    {
        return NotifyRelayCore.Safe.GenerateKeypair(_ctx);
    }

    public static string? GetPublicKey()
    {
        return NotifyRelayCore.Safe.GetPublicKey(_ctx);
    }

    public static int HasKeypair()
    {
        return NotifyRelayCore.Safe.HasKeypair(_ctx);
    }

    public static int DeriveSharedSecret(string deviceUuid, string peerPubKeyB64)
    {
        return NotifyRelayCore.Safe.DeriveSharedSecret(_ctx, deviceUuid, peerPubKeyB64);
    }

    public static string? ExportDeviceKey(string deviceUuid)
    {
        return NotifyRelayCore.Safe.ExportDeviceKey(_ctx, deviceUuid);
    }

    public static int ProcessLine(string line)
    {
        return NotifyRelayCore.Safe.ProcessLine(_ctx, line);
    }

    public static int PeriodicBroadcast(int action, string? uuid = null, string? name = null, int battery = -1, string? deviceType = null)
    {
        return NotifyRelayCore.Safe.PeriodicBroadcast(_ctx, action, uuid, name, battery, deviceType);
    }

    public static void SendHandshake(string uuid, string pubKey, string localIp, string targetIp, int battery, string deviceType)
    {
        NotifyRelayCore.Safe.SendHandshake(_ctx, uuid, pubKey, localIp, targetIp, battery, deviceType);
    }

    public static void SendPairingInit(string localUuid, string targetUuid, string expectedCode, int battery, string deviceType)
    {
        NotifyRelayCore.Safe.SendPairingInit(_ctx, localUuid, targetUuid, expectedCode, battery, deviceType);
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

    // ======== Pairing code management (Rust-generated) ========
    public static string? GeneratePairingCode(uint ttlSecs = 300)
    {
        return NotifyRelayCore.Safe.GeneratePairingCode(_ctx, ttlSecs);
    }

    public static void ClearPairingCode()
    {
        NotifyRelayCore.Safe.ClearPairingCode(_ctx);
    }

    public static int ValidatePairingCode(string code)
    {
        return NotifyRelayCore.Safe.ValidatePairingCode(_ctx, code);
    }

    public static void SendDataMessage(string header, string localUuid, string localPubKey, string remoteUuid, string plaintext)
    {
        NotifyRelayCore.Safe.SendDataMessage(_ctx, header, localUuid, localPubKey, remoteUuid, plaintext);
    }

    // ======== Network layer wrappers ========

    public static int StartTcpServer(ushort port)
    {
        return NotifyRelayCore.Safe.StartTcpServer(_ctx, port);
    }

    public static int StopTcpServer()
    {
        return NotifyRelayCore.Safe.StopTcpServer(_ctx);
    }



    public static int BroadcastMessage(string message)
    {
        return NotifyRelayCore.Safe.BroadcastMessage(_ctx, message);
    }

    public static int GetConnectedDeviceCount()
    {
        return NotifyRelayCore.Safe.GetConnectedDeviceCount(_ctx);
    }

    public static int IsDeviceConnected(string uuid)
    {
        return NotifyRelayCore.Safe.IsDeviceConnected(_ctx, uuid);
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

    public static string? ComputeFeatureIdSimple(string packageName, string title, string text)
    {
        return NotifyRelayCore.Safe.ComputeFeatureIdSimple(packageName, title, text);
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

    // ======== Callback-driven architecture ========

    private static PairedDevice? FindDevice(IntPtr uuidPtr)
    {
        var uuid = Marshal.PtrToStringUTF8(uuidPtr);
        return uuid != null ? DeviceManager?.FindDeviceById(uuid) : null;
    }

    private static string? PtrToString(IntPtr ptr) => Marshal.PtrToStringUTF8(ptr);

    public static void SetLogCallback(ILogger logger)
    {
        if (_gitHash != null)
        {
            logger.LogInformation("NotifyRelay Core loaded (git: {GitHash})", _gitHash);
            _gitHash = null;
        }
        NotifyRelayCore.OnLogCb cb = (level, messagePtr) =>
        {
            var msg = Marshal.PtrToStringUTF8(messagePtr);
            if (msg == null) return;
            var logLevel = level switch
            {
                1 => LogLevel.Error,
                2 => LogLevel.Warning,
                3 => LogLevel.Information,
                4 => LogLevel.Debug,
                5 => LogLevel.Trace,
                _ => LogLevel.Debug,
            };
            logger.Log(logLevel, "[Rust] {Msg}", msg);
        };
        var fp = Marshal.GetFunctionPointerForDelegate(cb);
        NotifyRelayCore.nrc_set_log_callback(fp);
        _callbackRefs.Add(cb);
    }

    public static void RegisterCallbacks()
    {
        if (_ctx == IntPtr.Zero) return;

        NotifyRelayCore.OnPairingCb onPairingCb = (uuidPtr, msgTypePtr, dataPtr, intValue, extraPtr, userData) =>
        {
            var uuid = Marshal.PtrToStringUTF8(uuidPtr);
            var msgType = Marshal.PtrToStringUTF8(msgTypePtr);
            var data = Marshal.PtrToStringUTF8(dataPtr);
            var extra = Marshal.PtrToStringUTF8(extraPtr);
            if (uuid == null || msgType == null) return;

            System.Diagnostics.Debug.WriteLine($"[CoreCb] on_pairing: type={msgType}, uuid={uuid}");

            switch (msgType)
            {
                case "HANDSHAKE":
                    {
                        if (data == null) return;
                        string pubKey = "", ip = "", deviceType = "unknown";
                        try
                        {
                            var doc = System.Text.Json.JsonDocument.Parse(data);
                            pubKey = doc.RootElement.GetProperty("pub_key").GetString() ?? "";
                            ip = doc.RootElement.GetProperty("ip").GetString() ?? "";
                            deviceType = doc.RootElement.GetProperty("device_type").GetString() ?? "unknown";
                        }
                        catch { }
                        var ns = NetworkService;
                        if (ns == null) return;
                        _ = ns.HandleHandshakeAsync(uuid, pubKey, ip, intValue, deviceType);
                    }
                    break;
                case "PAIRING_INIT":
                    {
                        System.Diagnostics.Debug.WriteLine("[CoreCb] on_pairing: PAIRING_INIT 进入");
                        if (data == null) return;
                        string spake2Pub = "", ip = "", deviceType = "unknown";
                        try
                        {
                            var doc = System.Text.Json.JsonDocument.Parse(data);
                            spake2Pub = doc.RootElement.GetProperty("spake2_pub").GetString() ?? "";
                            ip = doc.RootElement.GetProperty("ip").GetString() ?? "";
                            deviceType = doc.RootElement.GetProperty("device_type").GetString() ?? "unknown";
                        }
                        catch { }
                        var ns = NetworkService;
                        if (ns == null) { System.Diagnostics.Debug.WriteLine("[CoreCb] on_pairing: NetworkService=null"); return; }
                        System.Diagnostics.Debug.WriteLine("[CoreCb] on_pairing: PAIRING_INIT 调用 HandlePairingInitAsync");
                        _ = ns.HandlePairingInitAsync(uuid, spake2Pub, ip, intValue, deviceType);
                    }
                    break;
                case "PAIRING_RESP":
                    {
                        if (data == null) return;
                        string spake2Pub = "", ltPub = "", ip = "", deviceType = "unknown";
                        try
                        {
                            var doc = System.Text.Json.JsonDocument.Parse(data);
                            spake2Pub = doc.RootElement.GetProperty("spake2_pub").GetString() ?? "";
                            ltPub = doc.RootElement.GetProperty("lt_pub").GetString() ?? "";
                            ip = doc.RootElement.GetProperty("ip").GetString() ?? "";
                            deviceType = doc.RootElement.GetProperty("device_type").GetString() ?? "unknown";
                        }
                        catch { }
                        var ns = NetworkService;
                        if (ns == null) return;
                        _ = ns.HandlePairingRespAsync(uuid, spake2Pub, ltPub, ip, intValue, deviceType);
                    }
                    break;
                case "ACCEPT":
                    {
                        if (data == null) return;
                        string ltPubKey = "", ip = "", deviceType = "unknown";
                        try
                        {
                            var doc = System.Text.Json.JsonDocument.Parse(data);
                            ltPubKey = doc.RootElement.GetProperty("lt_pub_key").GetString() ?? "";
                            ip = doc.RootElement.GetProperty("ip").GetString() ?? "";
                            deviceType = doc.RootElement.GetProperty("device_type").GetString() ?? "unknown";
                        }
                        catch { }
                        var ns = NetworkService;
                        if (ns == null) return;
                        _ = ns.HandlePairingAcceptAsync(uuid, ltPubKey, ip, intValue, deviceType);
                    }
                    break;
                case "REJECT":
                    {
                        var ns = NetworkService;
                        if (ns == null) return;
                        _ = ns.HandleRejectAsync(uuid);
                    }
                    break;
                case "RESULT":
                    {
                        var ns = NetworkService;
                        if (ns == null) return;
                        _ = ns.HandlePairingResultAsync(uuid, intValue, extra ?? "");
                    }
                    break;
                case "HEARTBEAT_TCP":
                    {
                        var device = DeviceManager?.FindDeviceById(uuid);
                        if (device != null)
                        {
                            device.LastHeartbeat = DateTime.UtcNow;
                            if (!string.IsNullOrEmpty(extra))
                            {
                                device.Name = extra;
                                DeviceManager?.SaveDevice(device);
                            }
                        }
                    }
                    break;
            }
        };
        NotifyRelayCore.nrc_set_on_pairing_cb(_ctx, onPairingCb);
        _callbackRefs.Add(onPairingCb);

        NotifyRelayCore.OnDataCb onDataCb = (uuidPtr, msgTypePtr, plaintextPtr, userData) =>
        {
            var device = FindDevice(uuidPtr);
            var msgType = Marshal.PtrToStringUTF8(msgTypePtr);
            var text = Marshal.PtrToStringUTF8(plaintextPtr);
            System.Diagnostics.Debug.WriteLine($"[CoreCb] on_data: type={msgType}, uuid={device?.Id}, text_len={text?.Length}, device_found={device != null}");
            if (device == null || text == null || msgType == null) return;

            switch (msgType)
            {
                case "NOTIFICATION":
                    _ = ProtocolRouter?.OnDataNotificationAsync(device, text) ?? Task.CompletedTask;
                    break;
                case "MEDIAPLAY":
                    _ = ProtocolRouter?.OnDataMediaPlayAsync(device, text) ?? Task.CompletedTask;
                    break;
                case "ICON_REQUEST":
                    _ = ProtocolRouter?.OnDataIconRequestAsync(device, text) ?? Task.CompletedTask;
                    break;
                case "ICON_RESPONSE":
                    _ = ProtocolRouter?.OnDataIconResponseAsync(device, text) ?? Task.CompletedTask;
                    break;
                case "APP_LIST_REQUEST":
                    _ = ProtocolRouter?.OnDataAppListRequestAsync(device, text) ?? Task.CompletedTask;
                    break;
                case "APP_LIST_RESPONSE":
                    _ = ProtocolRouter?.OnDataAppListResponseAsync(device, text) ?? Task.CompletedTask;
                    break;
                case "MEDIA_CONTROL":
                    _ = ProtocolRouter?.OnDataMediaControlAsync(device, text) ?? Task.CompletedTask;
                    break;
                case "FTP":
                    _ = ProtocolRouter?.OnDataFtpAsync(device, text) ?? Task.CompletedTask;
                    break;
                case "CLIPBOARD":
                    _ = ProtocolRouter?.OnDataClipboardAsync(device, text) ?? Task.CompletedTask;
                    break;
                case "STATUS":
                    _ = ProtocolRouter?.OnDataStatusAsync(device, text) ?? Task.CompletedTask;
                    break;
                case "APP_LAUNCH":
                    _ = ProtocolRouter?.OnDataAppListRequestAsync(device, text) ?? Task.CompletedTask;
                    break;
                case "SUPERISLAND":
                    _ = ProtocolRouter?.OnDataSuperIslandAsync(device, text) ?? Task.CompletedTask;
                    break;
            }
        };
        NotifyRelayCore.nrc_set_on_data_cb(_ctx, onDataCb);
        _callbackRefs.Add(onDataCb);

        NotifyRelayCore.OnHeartbeatUdpCb onHeartbeatUdpCb = (uuidPtr, namePtr, port, battery, deviceTypePtr, ipPtr, userData) =>
        {
            var uuid = Marshal.PtrToStringUTF8(uuidPtr);
            var name = Marshal.PtrToStringUTF8(namePtr);
            var deviceType = Marshal.PtrToStringUTF8(deviceTypePtr) ?? "unknown";
            var ip = Marshal.PtrToStringUTF8(ipPtr);
            if (uuid == null) return;
            var hp = HeartbeatProcessor;
            if (hp == null) return;
            hp.HandleUdpHeartbeat(uuid, name, port, battery, deviceType, ip);
        };
        NotifyRelayCore.nrc_set_on_heartbeat_udp_cb(_ctx, onHeartbeatUdpCb);
        _callbackRefs.Add(onHeartbeatUdpCb);

        NotifyRelayCore.OnMdnsDiscoveredCb onMdnsDiscoveredCb = (uuidPtr, namePtr, ipPtr, port, battery, deviceTypePtr, userData) =>
        {
            var uuid = Marshal.PtrToStringUTF8(uuidPtr);
            var name = Marshal.PtrToStringUTF8(namePtr);
            var ip = Marshal.PtrToStringUTF8(ipPtr);
            var deviceType = Marshal.PtrToStringUTF8(deviceTypePtr) ?? "unknown";
            if (uuid == null || ip == null) return;
            System.Diagnostics.Debug.WriteLine($"[CoreCb] on_mdns_discovered: uuid={uuid}, ip={ip}, name={name}, port={port}, battery={battery}");

            var hp = HeartbeatProcessor;
            if (hp == null) return;
            hp.HandleMdnsDiscovered(uuid, name, ip, port, battery, deviceType);
        };
        NotifyRelayCore.nrc_set_on_mdns_discovered_cb(_ctx, onMdnsDiscoveredCb);
        _callbackRefs.Add(onMdnsDiscoveredCb);

        NotifyRelayCore.OnDeviceTimeoutCb onDeviceTimeoutCb = (uuidPtr, userData) =>
        {
            var uuid = Marshal.PtrToStringUTF8(uuidPtr);
            if (uuid == null) return;
            var dispatcher = App.MainWindow?.DispatcherQueue;
            if (dispatcher != null && !dispatcher.HasThreadAccess)
            {
                dispatcher.TryEnqueue(() => HandleDeviceTimeout(uuid));
            }
            else
            {
                HandleDeviceTimeout(uuid);
            }
        };
        NotifyRelayCore.nrc_set_on_device_timeout_cb(_ctx, onDeviceTimeoutCb);
        _callbackRefs.Add(onDeviceTimeoutCb);

        NotifyRelayCore.OnDeviceConnectedCb onDeviceConnectedCb = (uuidPtr, ipPtr, userData) =>
        {
            var uuid = Marshal.PtrToStringUTF8(uuidPtr);
            var ip = Marshal.PtrToStringUTF8(ipPtr) ?? "";
            if (uuid == null) return;
            System.Diagnostics.Debug.WriteLine($"[CoreCb] on_device_connected: uuid={uuid}, ip={ip}");
        };
        NotifyRelayCore.nrc_set_on_device_connected_cb(_ctx, onDeviceConnectedCb);
        _callbackRefs.Add(onDeviceConnectedCb);

        NotifyRelayCore.OnDeviceDisconnectedCb onDeviceDisconnectedCb = (uuidPtr, userData) =>
        {
            var uuid = Marshal.PtrToStringUTF8(uuidPtr);
            if (uuid == null) return;
            System.Diagnostics.Debug.WriteLine($"[CoreCb] on_device_disconnected: uuid={uuid}");
        };
        NotifyRelayCore.nrc_set_on_device_disconnected_cb(_ctx, onDeviceDisconnectedCb);
        _callbackRefs.Add(onDeviceDisconnectedCb);

        NotifyRelayCore.OnTcpErrorCb onTcpErrorCb = (errorPtr, userData) =>
        {
            var error = Marshal.PtrToStringUTF8(errorPtr) ?? "unknown";
            System.Diagnostics.Debug.WriteLine($"[CoreCb] on_tcp_error: {error}");
        };
        NotifyRelayCore.nrc_set_on_tcp_error_cb(_ctx, onTcpErrorCb);
        _callbackRefs.Add(onTcpErrorCb);
    }

    // ======== Heartbeat scheduler ========
    private static long _offlineDetectorHandle;
    private static long _senderQueueHandle;

    public static long StartHeartbeatScheduler(string uuid, string name, int battery, string deviceType, ulong intervalMs = 2000)
    {
        return NotifyRelayCore.Safe.StartHeartbeatScheduler(_ctx, uuid, name, battery, deviceType, intervalMs);
    }

    public static void UpdateHeartbeatSchedulerParams(string name, int battery, string deviceType)
    {
        NotifyRelayCore.Safe.UpdateHeartbeatSchedulerParams(_ctx, name, battery, deviceType);
    }

    public static void StopHeartbeatScheduler()
    {
        NotifyRelayCore.Safe.StopHeartbeatScheduler(_ctx);
    }

    // ======== Device state snapshot ========
    public static string? GetDeviceList(long authedTimeoutMs = 12000, long unauthedTimeoutMs = 5000)
    {
        return NotifyRelayCore.Safe.GetDeviceList(_ctx, authedTimeoutMs, unauthedTimeoutMs);
    }

    // ======== Offline detector ========
    public static long StartOfflineDetector(long timeoutSec = 12, ulong checkIntervalMs = 5000)
    {
        _offlineDetectorHandle = NotifyRelayCore.Safe.StartOfflineDetector(_ctx, timeoutSec, checkIntervalMs);
        return _offlineDetectorHandle;
    }

    // ======== Sender queue ========
    public static long CreateSenderQueue()
    {
        _senderQueueHandle = NotifyRelayCore.Safe.CreateSenderQueue(_ctx);
        return _senderQueueHandle;
    }

    public static void StartSenderQueue()
    {
        NotifyRelayCore.Safe.StartSenderQueue(_ctx, _senderQueueHandle);
    }

    public static void EnqueueMessage(string deviceUuid, string header, string plaintext, string? dedupKey = null)
    {
        NotifyRelayCore.Safe.EnqueueMessage(_ctx, _senderQueueHandle, deviceUuid, header, plaintext, dedupKey);
    }

    // 推送「全量」超级岛/媒体状态；Rust 内部 diff 并经 on_data 回调回传合并后的全量。
    public static void PushSuperIslandState(string deviceUuid, string fullJson, bool isEnd = false)
    {
        NotifyRelayCore.Safe.PushSuperIslandState(_ctx, _senderQueueHandle, deviceUuid, fullJson, isEnd);
    }

    public static void PushMediaState(string deviceUuid, string fullJson, bool isEnd = false)
    {
        NotifyRelayCore.Safe.PushMediaState(_ctx, _senderQueueHandle, deviceUuid, fullJson, isEnd);
    }

    public static void StopSenderQueue()
    {
        NotifyRelayCore.nrc_stop_sender_queue(_ctx, _senderQueueHandle);
        _senderQueueHandle = 0;
    }

    // ======== Clipboard ========
    public static string? ClipboardOnChanged(string targetsJson, string mime, string content, bool force)
    {
        return NotifyRelayCore.Safe.ClipboardOnChanged(_ctx, _senderQueueHandle, targetsJson, mime, content, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), force);
    }

    public static string? ClipboardOnReceived(string payloadJson)
    {
        return NotifyRelayCore.Safe.ClipboardOnReceived(_ctx, payloadJson, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
    }

    // ======== App sync (app list & icons) ========
    public static string? AppSyncPrepareIconRequest(string packagesJson, string installedJson, string cachedJson, string appDeviceJson, string sourceDeviceUuid)
    {
        return NotifyRelayCore.Safe.AppSyncPrepareIconRequest(_ctx, packagesJson, installedJson, cachedJson, appDeviceJson, sourceDeviceUuid, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
    }

    public static void AppSyncClearIconPending(string packagesJson)
    {
        NotifyRelayCore.Safe.AppSyncClearIconPending(_ctx, packagesJson);
    }

    public static string? AppSyncParseIconResponse(string payloadJson)
    {
        return NotifyRelayCore.Safe.AppSyncParseIconResponse(payloadJson);
    }

    public static string? AppSyncBuildApplistRequest(string scope = "user")
    {
        return NotifyRelayCore.Safe.AppSyncBuildApplistRequest(scope, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
    }

    public static string? AppSyncParseApplistResponse(string payloadJson)
    {
        return NotifyRelayCore.Safe.AppSyncParseApplistResponse(payloadJson);
    }

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

    public static void StartKnownDeviceScanner()
    {
        NotifyRelayCore.nrc_start_known_device_scanner(_ctx);
    }

    // ======== mDNS ========
    public static int StartMdnsAdvertiser(string uuid, string name, ushort port, string pubKey, string deviceType, int battery)
    {
        return NotifyRelayCore.Safe.StartMdnsAdvertiser(_ctx, uuid, name, port, pubKey, deviceType, battery);
    }

    public static void StopMdnsAdvertiser()
    {
        NotifyRelayCore.Safe.StopMdnsAdvertiser(_ctx);
    }

    public static int StartMdnsDiscovery()
    {
        return NotifyRelayCore.Safe.StartMdnsDiscovery(_ctx);
    }

    public static void StopMdnsDiscovery()
    {
        NotifyRelayCore.Safe.StopMdnsDiscovery(_ctx);
    }

    public static int AudioStart(string direction, int sampleRate, int channels, string remoteUuid)
    {
        return NotifyRelayCore.Safe.AudioStart(_ctx, direction, 23335, sampleRate, channels, remoteUuid);
    }

    public static int AudioWriteFrame(byte[] pcm)
    {
        return NotifyRelayCore.Safe.AudioWriteFrame(_ctx, pcm, pcm.Length);
    }

    public static int AudioStop()
    {
        return NotifyRelayCore.Safe.AudioStop(_ctx);
    }

    public static int AudioIsActive()
    {
        return NotifyRelayCore.Safe.AudioIsActive(_ctx);
    }

    public static void RegisterAudioCallbacks(NotifyRelayCore.AudioDataCallback dataCb, NotifyRelayCore.AudioEventCallback eventCb)
    {
        NotifyRelayCore.Safe.RegisterAudioDataCb(_ctx, dataCb);
        NotifyRelayCore.Safe.RegisterAudioEventCb(_ctx, eventCb);
        _callbackRefs.Add(dataCb);
        _callbackRefs.Add(eventCb);
    }

    private static void HandleDeviceTimeout(string uuid)
    {
        var device = DeviceManager?.FindDeviceById(uuid);
        if (device == null) return;
        device.ConnectionStatus = false;
        device.Session = null;
        NetworkService?.DisconnectDevice(uuid);
    }
}
