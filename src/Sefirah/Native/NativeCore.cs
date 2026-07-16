using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Extensions.Logging;
using NotifyRelay.Data.Contracts;
using NotifyRelay.Data.Models;
using NotifyRelay.Services;
using NotifyRelay.Services.Socket;

namespace NotifyRelay.Native;

public static class NativeCore
{
    private static IntPtr _ctx = IntPtr.Zero;
    private static bool _initialized = false;

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
    }

    public static void Destroy()
    {
        if (_ctx != IntPtr.Zero)
        {
            NotifyRelayCore.nrc_destroy(_ctx);
            _ctx = IntPtr.Zero;
        }
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

    public static void SendHandshake(string uuid, string pubKey, string ip, int battery, string deviceType)
    {
        NotifyRelayCore.Safe.SendHandshake(_ctx, uuid, pubKey, ip, battery, deviceType);
    }

    public static void SendPairingInit(string uuid, string expectedCode, string ip, int battery, string deviceType)
    {
        NotifyRelayCore.Safe.SendPairingInit(_ctx, uuid, expectedCode, ip, battery, deviceType);
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

    public static void SendHeartbeatTcp(string uuid, string name, ushort port, int battery, string deviceType)
    {
        NotifyRelayCore.Safe.SendHeartbeatTcp(_ctx, uuid, name, port, battery, deviceType);
    }

    public static void SendHeartbeatUdp(string uuid, string name, ushort port, int battery, string deviceType)
    {
        NotifyRelayCore.Safe.SendHeartbeatUdp(_ctx, uuid, name, port, battery, deviceType);
    }

    public static void SendDiscovery(string uuid, string name, ushort port, int battery, string deviceType)
    {
        NotifyRelayCore.Safe.SendDiscovery(_ctx, uuid, name, port, battery, deviceType);
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

    public static int SendToDevice(string uuid, string message)
    {
        return NotifyRelayCore.Safe.SendToDevice(_ctx, uuid, message);
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

        void Cb(Action<NotifyRelayCore.OnDataCb?> setter, string tag, Func<PairedDevice, string, Task> handler)
        {
            NotifyRelayCore.OnDataCb cb = (uuidPtr, textPtr, userData) =>
            {
                var device = FindDevice(uuidPtr);
                var text = PtrToString(textPtr);
                System.Diagnostics.Debug.WriteLine($"[CoreCb] {tag}: uuid={device?.Id}, text_len={text?.Length}, device_found={device != null}");
                if (device != null && text != null)
                {
                    Task ignored = handler(device, text);
                }
            };
            setter(cb); _callbackRefs.Add(cb);
        }

        Cb(cb => NotifyRelayCore.nrc_set_on_notification_cb(_ctx, cb), "DATA_NOTIFICATION",
            (d, t) => ProtocolRouter?.OnDataNotificationAsync(d, t) ?? Task.CompletedTask);
        Cb(cb => NotifyRelayCore.nrc_set_on_media_play_cb(_ctx, cb), "DATA_MEDIAPLAY",
            (d, t) => ProtocolRouter?.OnDataMediaPlayAsync(d, t) ?? Task.CompletedTask);
        Cb(cb => NotifyRelayCore.nrc_set_on_icon_request_cb(_ctx, cb), "DATA_ICON_REQUEST",
            (d, t) => ProtocolRouter?.OnDataIconRequestAsync(d, t) ?? Task.CompletedTask);
        Cb(cb => NotifyRelayCore.nrc_set_on_icon_response_cb(_ctx, cb), "DATA_ICON_RESPONSE",
            (d, t) => ProtocolRouter?.OnDataIconResponseAsync(d, t) ?? Task.CompletedTask);
        Cb(cb => NotifyRelayCore.nrc_set_on_app_list_request_cb(_ctx, cb), "DATA_APP_LIST_REQUEST",
            (d, t) => ProtocolRouter?.OnDataAppListRequestAsync(d, t) ?? Task.CompletedTask);
        Cb(cb => NotifyRelayCore.nrc_set_on_app_list_response_cb(_ctx, cb), "DATA_APP_LIST_RESPONSE",
            (d, t) => ProtocolRouter?.OnDataAppListResponseAsync(d, t) ?? Task.CompletedTask);
        Cb(cb => NotifyRelayCore.nrc_set_on_media_control_cb(_ctx, cb), "DATA_MEDIA_CONTROL",
            (d, t) => ProtocolRouter?.OnDataMediaControlAsync(d, t) ?? Task.CompletedTask);
        Cb(cb => NotifyRelayCore.nrc_set_on_ftp_cb(_ctx, cb), "DATA_FTP",
            (d, t) => ProtocolRouter?.OnDataFtpAsync(d, t) ?? Task.CompletedTask);
        Cb(cb => NotifyRelayCore.nrc_set_on_clipboard_cb(_ctx, cb), "DATA_CLIPBOARD",
            (d, t) => ProtocolRouter?.OnDataClipboardAsync(d, t) ?? Task.CompletedTask);
        Cb(cb => NotifyRelayCore.nrc_set_on_status_cb(_ctx, cb), "DATA_STATUS",
            (d, t) => ProtocolRouter?.OnDataStatusAsync(d, t) ?? Task.CompletedTask);
        Cb(cb => NotifyRelayCore.nrc_set_on_app_launch_cb(_ctx, cb), "DATA_APP_LAUNCH",
            (d, t) => ProtocolRouter?.OnDataAppListRequestAsync(d, t) ?? Task.CompletedTask);
        Cb(cb => NotifyRelayCore.nrc_set_on_superisland_cb(_ctx, cb), "DATA_SUPERISLAND",
            (d, t) => ProtocolRouter?.OnDataSuperIslandAsync(d, t) ?? Task.CompletedTask);
        Cb(cb => NotifyRelayCore.nrc_set_on_unknown_data_cb(_ctx, cb), "DATA_UNKNOWN",
            (d, t) => Task.CompletedTask);

        // ==================== 非 DATA 回调注册 ====================

        // ---- on_handshake ----
        {
            NotifyRelayCore.OnHandshakeCb cb = (uuidPtr, pubKeyPtr, ipPtr, battery, deviceTypePtr, userData) =>
            {
                var uuid = Marshal.PtrToStringUTF8(uuidPtr);
                var pubKey = Marshal.PtrToStringUTF8(pubKeyPtr);
                var ip = Marshal.PtrToStringUTF8(ipPtr) ?? "";
                var deviceType = Marshal.PtrToStringUTF8(deviceTypePtr) ?? "unknown";
                if (uuid == null || pubKey == null) return;
                var ns = NetworkService;
                if (ns == null) return;
                _ = ns.HandleHandshakeAsync(uuid, pubKey, ip, battery, deviceType);
            };
            NotifyRelayCore.nrc_set_on_handshake_cb(_ctx, cb); _callbackRefs.Add(cb);
        }

        // ---- on_pairing_init ----
        {
            NotifyRelayCore.OnPairingInitCb cb = (uuidPtr, tmpPubKeyPtr, ipPtr, battery, deviceTypePtr, userData) =>
            {
                System.Diagnostics.Debug.WriteLine("[CoreCb] on_pairing_init 进入");
                var uuid = Marshal.PtrToStringUTF8(uuidPtr);
                var tmpPubKey = Marshal.PtrToStringUTF8(tmpPubKeyPtr);
                var ip = Marshal.PtrToStringUTF8(ipPtr) ?? "";
                var deviceType = Marshal.PtrToStringUTF8(deviceTypePtr) ?? "unknown";
                System.Diagnostics.Debug.WriteLine($"[CoreCb] on_pairing_init: uuid={uuid}, ip={ip}");
                if (uuid == null || tmpPubKey == null) { System.Diagnostics.Debug.WriteLine("[CoreCb] on_pairing_init: uuid/tmpPubKey=null"); return; }
                var ns = NetworkService;
                if (ns == null) { System.Diagnostics.Debug.WriteLine("[CoreCb] on_pairing_init: NetworkService=null"); return; }
                System.Diagnostics.Debug.WriteLine("[CoreCb] on_pairing_init: 调用 HandlePairingInitAsync");
                _ = ns.HandlePairingInitAsync(uuid, tmpPubKey, ip, battery, deviceType);
            };
            NotifyRelayCore.nrc_set_on_pairing_init_cb(_ctx, cb); _callbackRefs.Add(cb);
        }

        // ---- on_pairing_resp ----
        {
            NotifyRelayCore.OnPairingRespCb cb = (uuidPtr, tmpPubPtr, ltPubPtr, encryptedCodePtr, ipPtr, battery, deviceTypePtr, userData) =>
            {
                var uuid = Marshal.PtrToStringUTF8(uuidPtr);
                var tmpPub = Marshal.PtrToStringUTF8(tmpPubPtr);
                var ltPub = Marshal.PtrToStringUTF8(ltPubPtr);
                var encCode = Marshal.PtrToStringUTF8(encryptedCodePtr);
                var ip = Marshal.PtrToStringUTF8(ipPtr) ?? "";
                var deviceType = Marshal.PtrToStringUTF8(deviceTypePtr) ?? "unknown";
                if (uuid == null || tmpPub == null || ltPub == null || encCode == null) return;
                var ns = NetworkService;
                if (ns == null) return;
                _ = ns.HandlePairingRespAsync(uuid, tmpPub, ltPub, encCode, ip, battery, deviceType);
            };
            NotifyRelayCore.nrc_set_on_pairing_resp_cb(_ctx, cb); _callbackRefs.Add(cb);
        }

        // ---- on_accept ----
        {
            NotifyRelayCore.OnAcceptCb cb = (uuidPtr, ltPubKeyPtr, ipPtr, battery, deviceTypePtr, userData) =>
            {
                var uuid = Marshal.PtrToStringUTF8(uuidPtr);
                var ltPubKey = Marshal.PtrToStringUTF8(ltPubKeyPtr);
                var ip = Marshal.PtrToStringUTF8(ipPtr) ?? "";
                var deviceType = Marshal.PtrToStringUTF8(deviceTypePtr) ?? "unknown";
                if (uuid == null || ltPubKey == null) return;
                var ns = NetworkService;
                if (ns == null) return;
                _ = ns.HandlePairingAcceptAsync(uuid, ltPubKey, ip, battery, deviceType);
            };
            NotifyRelayCore.nrc_set_on_accept_cb(_ctx, cb); _callbackRefs.Add(cb);
        }

        // ---- on_reject ----
        {
            NotifyRelayCore.OnRejectCb cb = (uuidPtr, userData) =>
            {
                var uuid = Marshal.PtrToStringUTF8(uuidPtr);
                if (uuid == null) return;
                var ns = NetworkService;
                if (ns == null) return;
                _ = ns.HandleRejectAsync(uuid);
            };
            NotifyRelayCore.nrc_set_on_reject_cb(_ctx, cb); _callbackRefs.Add(cb);
        }

        // ---- on_heartbeat_udp ----
        {
            NotifyRelayCore.OnHeartbeatUdpCb cb = (uuidPtr, namePtr, port, battery, deviceTypePtr, userData) =>
            {
                var uuid = Marshal.PtrToStringUTF8(uuidPtr);
                var name = Marshal.PtrToStringUTF8(namePtr);
                var deviceType = Marshal.PtrToStringUTF8(deviceTypePtr) ?? "unknown";
                if (uuid == null) return;
                var hp = HeartbeatProcessor;
                if (hp == null) return;
                hp.HandleUdpHeartbeat(uuid, name, port, battery, deviceType);
            };
            NotifyRelayCore.nrc_set_on_heartbeat_udp_cb(_ctx, cb); _callbackRefs.Add(cb);
        }

        // ---- on_heartbeat_tcp ----
        {
            NotifyRelayCore.OnHeartbeatTcpCb cb = (uuidPtr, namePtr, port, battery, deviceTypePtr, ipPtr, userData) =>
            {
                var uuid = Marshal.PtrToStringUTF8(uuidPtr);
                var name = Marshal.PtrToStringUTF8(namePtr);
                var deviceType = Marshal.PtrToStringUTF8(deviceTypePtr) ?? "unknown";
                if (uuid == null) return;
                var device = DeviceManager?.FindDeviceById(uuid);
                if (device != null)
                {
                    device.LastHeartbeat = DateTime.UtcNow;
                    if (!string.IsNullOrEmpty(name))
                    {
                        device.Name = name;
                        DeviceManager?.SaveDevice(device);
                    }
                }
            };
            NotifyRelayCore.nrc_set_on_heartbeat_tcp_cb(_ctx, cb); _callbackRefs.Add(cb);
        }

        // ---- on_device_timeout ----
        {
            NotifyRelayCore.OnDeviceTimeoutCb cb = (uuidPtr, userData) =>
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
            NotifyRelayCore.nrc_set_on_device_timeout_cb(_ctx, cb); _callbackRefs.Add(cb);
        }

        // ---- on_device_connected ----
        {
            NotifyRelayCore.OnDeviceConnectedCb cb = (uuidPtr, ipPtr, userData) =>
            {
                var uuid = Marshal.PtrToStringUTF8(uuidPtr);
                var ip = Marshal.PtrToStringUTF8(ipPtr) ?? "";
                if (uuid == null) return;
                var ns = NetworkService;
                if (ns == null) return;
                // 设备连接事件由 Rust 内核处理，平台端只需记录日志
                System.Diagnostics.Debug.WriteLine($"[CoreCb] on_device_connected: uuid={uuid}, ip={ip}");
            };
            NotifyRelayCore.nrc_set_on_device_connected_cb(_ctx, cb); _callbackRefs.Add(cb);
        }

        // ---- on_device_disconnected ----
        {
            NotifyRelayCore.OnDeviceDisconnectedCb cb = (uuidPtr, userData) =>
            {
                var uuid = Marshal.PtrToStringUTF8(uuidPtr);
                if (uuid == null) return;
                var ns = NetworkService;
                if (ns == null) return;
                // 设备断开事件由 Rust 内核处理，平台端只需记录日志
                System.Diagnostics.Debug.WriteLine($"[CoreCb] on_device_disconnected: uuid={uuid}");
            };
            NotifyRelayCore.nrc_set_on_device_disconnected_cb(_ctx, cb); _callbackRefs.Add(cb);
        }

        // ---- on_tcp_error ----
        {
            NotifyRelayCore.OnTcpErrorCb cb = (errorPtr, userData) =>
            {
                var error = Marshal.PtrToStringUTF8(errorPtr) ?? "unknown";
                System.Diagnostics.Debug.WriteLine($"[CoreCb] on_tcp_error: {error}");
            };
            NotifyRelayCore.nrc_set_on_tcp_error_cb(_ctx, cb); _callbackRefs.Add(cb);
        }
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
