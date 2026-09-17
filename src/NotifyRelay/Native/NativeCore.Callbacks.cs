using System.Runtime.InteropServices;
using NotifyRelay.Data.Models;

namespace NotifyRelay.Native;

public static partial class NativeCore
{
    // ======== Callback-driven architecture ========

    private static PairedDevice? FindDevice(IntPtr uuidPtr)
    {
        var uuid = Marshal.PtrToStringUTF8(uuidPtr);
        return uuid != null ? DeviceManager?.FindDeviceById(uuid) : null;
    }

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

            // HEARTBEAT_TCP 是维持性心跳（每设备周期连接），静默；其余为真实配对事件，仅状态变化时打印
            if (msgType != "HEARTBEAT_TCP")
            {
                System.Diagnostics.Debug.WriteLine($"[CoreCb] 收到配对事件: 类型={msgType}, 设备={uuid}");
            }

            switch (msgType)
            {
                case "HANDSHAKE":
                    {
                        if (data == null) return;
                        string pubKey = "", ip = "", deviceType = "unknown";
                        bool autoAccept = false;
                        try
                        {
                            using var doc = System.Text.Json.JsonDocument.Parse(data);
                            pubKey = doc.RootElement.GetProperty("pub_key").GetString() ?? "";
                            ip = doc.RootElement.GetProperty("ip").GetString() ?? "";
                            deviceType = doc.RootElement.GetProperty("device_type").GetString() ?? "unknown";
                            if (doc.RootElement.TryGetProperty("auto_accept", out var aa) && aa.ValueKind == System.Text.Json.JsonValueKind.True)
                                autoAccept = true;
                        }
                        catch { }
                        var ns = NetworkService;
                        if (ns == null) return;
                        _ = ns.HandleHandshakeAsync(uuid, pubKey, ip, intValue, deviceType, autoAccept);
                    }
                    break;
                case "PAIRING_INIT":
                    {
                        if (data == null) return;
                        string spake2Pub = "", ip = "", deviceType = "unknown";
                        try
                        {
                            using var doc = System.Text.Json.JsonDocument.Parse(data);
                            spake2Pub = doc.RootElement.GetProperty("spake2_pub").GetString() ?? "";
                            ip = doc.RootElement.GetProperty("ip").GetString() ?? "";
                            deviceType = doc.RootElement.GetProperty("device_type").GetString() ?? "unknown";
                        }
                        catch { }
                        var ns = NetworkService;
                        if (ns == null) return;
                        _ = ns.HandlePairingInitAsync(uuid, spake2Pub, ip, intValue, deviceType);
                    }
                    break;
                case "PAIRING_RESP":
                    {
                        if (data == null) return;
                        string spake2Pub = "", ltPub = "", ip = "", deviceType = "unknown";
                        try
                        {
                            using var doc = System.Text.Json.JsonDocument.Parse(data);
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
                            using var doc = System.Text.Json.JsonDocument.Parse(data);
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
                    // 运行时状态（名称/电量/在线/IP/类型）全部由 Rust core 的 DeviceRegistry 维护，
                    // 平台端不再镜像：此处仅触发一次快照刷新（HeartbeatProcessor 内部按最小间隔节流）。
                    HeartbeatProcessor?.NotifyDeviceListChanged();
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
            System.Diagnostics.Debug.WriteLine($"[CoreCb] 收到数据: 类型={msgType}, 设备={device?.Id}, 长度={text?.Length}, 设备匹配={device != null}");
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

        // ---- on_state_query (超级岛/媒体心跳查询回调：0=不存在 / 1=存在无变更 / 2=存在有变更) ----
        // 运行在 Rust 心跳线程且锁已释放；PC 仅作为媒体发送端：
        // 媒体会话存在性由 WindowsPlaybackService 提供（MediaSessionQueryHandler），
        // 无活跃会话 → 0（Rust 移除会话）；有 → 1 保活（状态变更由事件驱动推送）。
        NotifyRelayCore.OnStateQueryCb onStateQueryCb = (uuidPtr, featureIdPtr, isMedia, userData) =>
        {
            var uuid = Marshal.PtrToStringUTF8(uuidPtr);
            var featureId = Marshal.PtrToStringUTF8(featureIdPtr);
            if (uuid == null || featureId == null) return 0;
            if (isMedia == 0) return 0; // PC 仅发送媒体会话，无超级岛会话
            try
            {
                return MediaSessionQueryHandler?.Invoke(uuid) == true ? 1 : 0;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[CoreCb] on_state_query error: {ex.Message}");
                return 1; // 异常保守保活，等待下一次查询
            }
        };
        NotifyRelayCore.nrc_set_on_state_query_cb(_ctx, onStateQueryCb);
        _callbackRefs.Add(onStateQueryCb);

        // ---- on_device_discovered（TCP 扫描发现）----
        // 设备状态（名称解码、状态登记、可见性）全部由 Rust core 负责，
        // 此处不解析业务字段，仅通知平台端重新拉取设备快照刷新 UI。
        NotifyRelayCore.OnDeviceDiscoveredCb onDeviceDiscoveredCb = (uuidPtr, namePtr, port, battery, deviceTypePtr, ipPtr, userData) =>
        {
            var uuid = Marshal.PtrToStringUTF8(uuidPtr);
            var ip = Marshal.PtrToStringUTF8(ipPtr) ?? "";
            if (uuid == null) return;
            // 调试日志（保留代码，需要排查发现流程时再放开）
            //System.Diagnostics.Debug.WriteLine($"[CoreCb] 扫描发现设备: {uuid}, ip={ip}, 端口={port}, 电量={battery}");

            HeartbeatProcessor?.NotifyDeviceListChanged();
        };
        NotifyRelayCore.nrc_set_on_device_discovered_cb(_ctx, onDeviceDiscoveredCb);
        _callbackRefs.Add(onDeviceDiscoveredCb);

        NotifyRelayCore.OnDeviceTimeoutCb onDeviceTimeoutCb = (uuidPtr, userData) =>
        {
            var uuid = Marshal.PtrToStringUTF8(uuidPtr);
            if (uuid == null) return;
            // 设备真离线（超时未响应），打印并清除在线状态，下次重新上线时"设备已连接"会再次打印
            if (_deviceOnline.TryRemove(uuid, out _))
            {
                System.Diagnostics.Debug.WriteLine($"[CoreCb] 设备已离线: {uuid}");
            }
            // 设备离线同样由 core 快照反映，通知平台端刷新列表
            HeartbeatProcessor?.NotifyDeviceListChanged();

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
            // 仅首次上线（或离线后重新上线）打印；维持性心跳连接的反复上下线静默
            if (_deviceOnline.TryAdd(uuid, 0))
            {
                System.Diagnostics.Debug.WriteLine($"[CoreCb] 设备已连接: {uuid} ({ip})");
            }
        };
        NotifyRelayCore.nrc_set_on_device_connected_cb(_ctx, onDeviceConnectedCb);
        _callbackRefs.Add(onDeviceConnectedCb);

        NotifyRelayCore.OnDeviceDisconnectedCb onDeviceDisconnectedCb = (uuidPtr, userData) =>
        {
            // 心跳维持连接的断开不打印，真实离线由 on_device_timeout（超时检测）体现
        };
        NotifyRelayCore.nrc_set_on_device_disconnected_cb(_ctx, onDeviceDisconnectedCb);
        _callbackRefs.Add(onDeviceDisconnectedCb);

        NotifyRelayCore.OnTcpErrorCb onTcpErrorCb = (errorPtr, userData) =>
        {
            var error = Marshal.PtrToStringUTF8(errorPtr) ?? "unknown";
            System.Diagnostics.Debug.WriteLine($"[CoreCb] TCP 错误: {error}");
        };
        NotifyRelayCore.nrc_set_on_tcp_error_cb(_ctx, onTcpErrorCb);
        _callbackRefs.Add(onTcpErrorCb);
    }
}
