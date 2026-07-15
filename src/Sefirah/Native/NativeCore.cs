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

    /// <summary>供非 DATA 回调获取当前 TCP 会话上下文</summary>
    internal static AsyncLocal<ServerSession?> CurrentSession { get; } = new();

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

    public static string? DecryptMessage(string encryptedLine)
    {
        return NotifyRelayCore.Safe.DecryptMessage(_ctx, encryptedLine);
    }

    public static string? DecryptPayload(string localUuid, string encryptedB64)
    {
        return NotifyRelayCore.Safe.DecryptPayload(_ctx, localUuid, encryptedB64);
    }

    public static string? FormatHeartbeat(string uuid, string nameB64, ushort port, int battery, string deviceType)
    {
        return NotifyRelayCore.Safe.FormatHeartbeat(uuid, nameB64, port, battery, deviceType);
    }

    public static string? FormatDiscovery(string uuid, string nameB64, ushort port, int battery, string deviceType)
    {
        return NotifyRelayCore.Safe.FormatDiscovery(uuid, nameB64, port, battery, deviceType);
    }

    public static string? FormatTcpHeartbeat(string uuid, string nameB64, ushort port, int battery, string deviceType)
    {
        return NotifyRelayCore.Safe.FormatTcpHeartbeat(uuid, nameB64, port, battery, deviceType);
    }

    public static string? FormatPairingInit(string uuid, string tmpPubKey, string ip, int battery, string deviceType)
    {
        return NotifyRelayCore.Safe.FormatPairingInit(uuid, tmpPubKey, ip, battery, deviceType);
    }

    public static string? FormatPairingResp(string uuid, string tmpPub, string ltPub, string encryptedCode, string ip, int battery, string deviceType)
    {
        return NotifyRelayCore.Safe.FormatPairingResp(uuid, tmpPub, ltPub, encryptedCode, ip, battery, deviceType);
    }

    public static string? FormatAccept(string uuid, string ltPubKey, string ip, int battery, string deviceType)
    {
        return NotifyRelayCore.Safe.FormatAccept(uuid, ltPubKey, ip, battery, deviceType);
    }

    public static string? FormatHandshake(string uuid, string pubKey, string ip, int battery, string deviceType)
    {
        return NotifyRelayCore.Safe.FormatHandshake(uuid, pubKey, ip, battery, deviceType);
    }

    public static string? DecodeLine(string line)
    {
        return NotifyRelayCore.Safe.DecodeLine(_ctx, line);
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

    public static int GenerateEphemeralKeypair()
    {
        return NotifyRelayCore.Safe.GenerateEphemeralKeypair(_ctx);
    }

    public static string? GetEphemeralPublicKey()
    {
        return NotifyRelayCore.Safe.GetEphemeralPublicKey(_ctx);
    }

    public static int HasEphemeralKeypair()
    {
        return NotifyRelayCore.Safe.HasEphemeralKeypair(_ctx);
    }

    public static void ClearEphemeralKeypair()
    {
        NotifyRelayCore.Safe.ClearEphemeralKeypair(_ctx);
    }

    public static int DeriveSharedSecret(string deviceUuid, string peerPubKeyB64)
    {
        return NotifyRelayCore.Safe.DeriveSharedSecret(_ctx, deviceUuid, peerPubKeyB64);
    }

    public static int DerivePairingKey(string peerEphPubB64)
    {
        return NotifyRelayCore.Safe.DerivePairingKey(_ctx, peerEphPubB64);
    }

    public static string? EncryptPairingCode(string code)
    {
        return NotifyRelayCore.Safe.EncryptPairingCode(_ctx, code);
    }

    public static string? DecryptPairingCode(string encryptedB64)
    {
        return NotifyRelayCore.Safe.DecryptPairingCode(_ctx, encryptedB64);
    }

    public static int DeriveLongTermKey(string peerUuid, string peerLtPubB64)
    {
        return NotifyRelayCore.Safe.DeriveLongTermKey(_ctx, peerUuid, peerLtPubB64);
    }

    public static string? ExportDeviceKey(string deviceUuid)
    {
        return NotifyRelayCore.Safe.ExportDeviceKey(_ctx, deviceUuid);
    }

    public static string? ExportLocalKeypair()
    {
        return NotifyRelayCore.Safe.ExportLocalKeypair(_ctx);
    }

    public static int ProcessLine(string line)
    {
        return NotifyRelayCore.Safe.ProcessLine(_ctx, line);
    }

    public static int ProcessUdpBroadcast(string line)
    {
        return NotifyRelayCore.Safe.ProcessUdpBroadcast(_ctx, line);
    }

    public static void SendHandshake(string uuid, string pubKey, string ip, int battery, string deviceType)
    {
        NotifyRelayCore.Safe.SendHandshake(_ctx, uuid, pubKey, ip, battery, deviceType);
    }

    public static void SendPairingInit(string uuid, string ip, int battery, string deviceType)
    {
        NotifyRelayCore.Safe.SendPairingInit(_ctx, uuid, ip, battery, deviceType);
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

    // ======== New function wrappers ========

    public static int VerifyPairingCode(string storedCode, string inputCode)
    {
        return NotifyRelayCore.Safe.VerifyPairingCode(storedCode, inputCode);
    }

    public static string? ComputeDedupKey(string deviceUuid, string data)
    {
        return NotifyRelayCore.Safe.ComputeDedupKey(deviceUuid, data);
    }

    public static bool HeartbeatHasTimedOut(long lastHeartbeatSec, long nowSec, long timeoutSec)
    {
        return NotifyRelayCore.Safe.HeartbeatHasTimedOut(lastHeartbeatSec, nowSec, timeoutSec) != 0;
    }

    public static int HeartbeatTick(long timeoutSec)
    {
        return NotifyRelayCore.Safe.HeartbeatTick(_ctx, timeoutSec);
    }

    public static string? ComputeFeatureId(string packageName, string title, string text)
    {
        return NotifyRelayCore.Safe.ComputeFeatureId(packageName, title, text);
    }

    public static int ParseHeartbeatWithCb(string line, NotifyRelayCore.OnHeartbeatWithCb cb, IntPtr userData)
    {
        return NotifyRelayCore.Safe.ParseHeartbeatWithCb(line, cb, userData);
    }

    public static int ParseHeartbeatTcpWithCb(string line, NotifyRelayCore.OnHeartbeatTcpWithCb cb, IntPtr userData)
    {
        return NotifyRelayCore.Safe.ParseHeartbeatTcpWithCb(line, cb, userData);
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
                var session = CurrentSession.Value;
                if (session == null) return;
                var uuid = Marshal.PtrToStringUTF8(uuidPtr);
                var pubKey = Marshal.PtrToStringUTF8(pubKeyPtr);
                var ip = Marshal.PtrToStringUTF8(ipPtr) ?? "";
                var deviceType = Marshal.PtrToStringUTF8(deviceTypePtr) ?? "unknown";
                if (uuid == null || pubKey == null) return;
                var ns = NetworkService;
                if (ns == null) return;
                _ = ns.HandleHandshakeAsync(session, uuid, pubKey, ip, battery, deviceType);
            };
            NotifyRelayCore.nrc_set_on_handshake_cb(_ctx, cb); _callbackRefs.Add(cb);
        }

        // ---- on_pairing_init ----
        {
            NotifyRelayCore.OnPairingInitCb cb = (uuidPtr, tmpPubKeyPtr, ipPtr, battery, deviceTypePtr, userData) =>
            {
                var session = CurrentSession.Value;
                if (session == null) return;
                var uuid = Marshal.PtrToStringUTF8(uuidPtr);
                var tmpPubKey = Marshal.PtrToStringUTF8(tmpPubKeyPtr);
                var ip = Marshal.PtrToStringUTF8(ipPtr) ?? "";
                var deviceType = Marshal.PtrToStringUTF8(deviceTypePtr) ?? "unknown";
                if (uuid == null || tmpPubKey == null) return;
                var ns = NetworkService;
                if (ns == null) return;
                _ = ns.HandlePairingInitAsync(session, uuid, tmpPubKey, ip, battery, deviceType);
            };
            NotifyRelayCore.nrc_set_on_pairing_init_cb(_ctx, cb); _callbackRefs.Add(cb);
        }

        // ---- on_pairing_resp ----
        {
            NotifyRelayCore.OnPairingRespCb cb = (uuidPtr, tmpPubPtr, ltPubPtr, encryptedCodePtr, ipPtr, battery, deviceTypePtr, userData) =>
            {
                var session = CurrentSession.Value;
                if (session == null) return;
                var uuid = Marshal.PtrToStringUTF8(uuidPtr);
                var tmpPub = Marshal.PtrToStringUTF8(tmpPubPtr);
                var ltPub = Marshal.PtrToStringUTF8(ltPubPtr);
                var encCode = Marshal.PtrToStringUTF8(encryptedCodePtr);
                var ip = Marshal.PtrToStringUTF8(ipPtr) ?? "";
                var deviceType = Marshal.PtrToStringUTF8(deviceTypePtr) ?? "unknown";
                if (uuid == null || tmpPub == null || ltPub == null || encCode == null) return;
                var ns = NetworkService;
                if (ns == null) return;
                _ = ns.HandlePairingRespAsync(session, uuid, tmpPub, ltPub, encCode, ip, battery, deviceType);
            };
            NotifyRelayCore.nrc_set_on_pairing_resp_cb(_ctx, cb); _callbackRefs.Add(cb);
        }

        // ---- on_accept ----
        {
            NotifyRelayCore.OnAcceptCb cb = (uuidPtr, ltPubKeyPtr, ipPtr, battery, deviceTypePtr, userData) =>
            {
                var session = CurrentSession.Value;
                if (session == null) return;
                var uuid = Marshal.PtrToStringUTF8(uuidPtr);
                var ltPubKey = Marshal.PtrToStringUTF8(ltPubKeyPtr);
                var ip = Marshal.PtrToStringUTF8(ipPtr) ?? "";
                var deviceType = Marshal.PtrToStringUTF8(deviceTypePtr) ?? "unknown";
                if (uuid == null || ltPubKey == null) return;
                var ns = NetworkService;
                if (ns == null) return;
                _ = ns.HandlePairingAcceptAsync(session, uuid, ltPubKey, ip, battery, deviceType);
            };
            NotifyRelayCore.nrc_set_on_accept_cb(_ctx, cb); _callbackRefs.Add(cb);
        }

        // ---- on_reject ----
        {
            NotifyRelayCore.OnRejectCb cb = (uuidPtr, userData) =>
            {
                var session = CurrentSession.Value;
                if (session == null) return;
                var uuid = Marshal.PtrToStringUTF8(uuidPtr);
                if (uuid == null) return;
                var ns = NetworkService;
                if (ns == null) return;
                _ = ns.HandleRejectAsync(session, uuid);
            };
            NotifyRelayCore.nrc_set_on_reject_cb(_ctx, cb); _callbackRefs.Add(cb);
        }

        // ---- on_heartbeat_udp ----
        {
            NotifyRelayCore.OnHeartbeatUdpCb cb = (uuidPtr, nameB64Ptr, port, battery, deviceTypePtr, userData) =>
            {
                var uuid = Marshal.PtrToStringUTF8(uuidPtr);
                var nameB64 = Marshal.PtrToStringUTF8(nameB64Ptr);
                var deviceType = Marshal.PtrToStringUTF8(deviceTypePtr) ?? "unknown";
                if (uuid == null) return;
                var hp = HeartbeatProcessor;
                if (hp == null) return;
                hp.HandleUdpHeartbeat(uuid, nameB64, port, battery, deviceType);
            };
            NotifyRelayCore.nrc_set_on_heartbeat_udp_cb(_ctx, cb); _callbackRefs.Add(cb);
        }

        // ---- on_heartbeat_tcp ----
        {
            NotifyRelayCore.OnHeartbeatTcpCb cb = (uuidPtr, nameB64Ptr, port, battery, deviceTypePtr, ipPtr, userData) =>
            {
                var uuid = Marshal.PtrToStringUTF8(uuidPtr);
                var deviceType = Marshal.PtrToStringUTF8(deviceTypePtr) ?? "unknown";
                if (uuid == null) return;
                var device = DeviceManager?.FindDeviceById(uuid);
                if (device != null)
                {
                    device.LastHeartbeat = DateTime.UtcNow;
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

        // ---- on_send (统一发送回调：写入当前 TCP 会话) ----
        {
            NotifyRelayCore.OnSendCb cb = (linePtr, userData) =>
            {
                var session = CurrentSession.Value;
                if (session == null) return;
                var line = Marshal.PtrToStringUTF8(linePtr);
                if (line == null) return;
                try
                {
                    var data = System.Text.Encoding.UTF8.GetBytes(line + "\n");
                    session.Send(data, 0, data.Length);
                }
                catch { }
            };
            NotifyRelayCore.nrc_set_on_send_cb(_ctx, cb); _callbackRefs.Add(cb);
        }

        // ---- on_send_udp (统一 UDP 发送回调) ----
        {
            NotifyRelayCore.OnSendCb cb = (linePtr, userData) =>
            {
                var line = Marshal.PtrToStringUTF8(linePtr);
                if (line == null) return;
                try
                {
                    var data = System.Text.Encoding.UTF8.GetBytes(line);
                    var client = new System.Net.Sockets.UdpClient();
                    client.EnableBroadcast = true;
                    client.Send(data, data.Length, new System.Net.IPEndPoint(System.Net.IPAddress.Broadcast, 23334));
                    client.Close();
                }
                catch { }
            };
            NotifyRelayCore.nrc_set_on_send_udp_cb(_ctx, cb); _callbackRefs.Add(cb);
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
