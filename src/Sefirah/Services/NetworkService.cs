using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text;
using CommunityToolkit.WinUI;
using NotifyRelay.Data.AppDatabase.Models;
using NotifyRelay.Data.AppDatabase.Repository;
using NotifyRelay.Data.Contracts;
using NotifyRelay.Data.Models;
using NotifyRelay.Helpers;
using NotifyRelay.Native;
using NotifyRelay.Services.Socket;
using Uno.Logging;
using Windows.Data.Xml.Dom;
using Windows.UI.Notifications;
using NotifyRelay.Dialogs;


namespace NotifyRelay.Services;

public class NetworkService(
    ILogger<NetworkService> logger,
    IDeviceManager deviceManager,
    IAdbService adbService,
    IScreenMirrorService screenMirrorService,
    ISystemInfoService systemInfoService,
    ProtocolRouter protocolRouter,
    ServerLineRouter serverLineRouter,
    HeartbeatProcessor heartbeatProcessor,
    IProtocolSender protocolSender,
    Func<IRemoteAppService> remoteAppServiceFactory) : INetworkService, ISessionManager, ITcpServerProvider
{
    private Server? server;
    public int ServerPort { get; private set; } = 23333;
    private bool isRunning;


    private readonly ConcurrentDictionary<Guid, string> sessionBuffers = new();
    private readonly Dictionary<string, ServerSession> deviceSessions = new();
    private readonly Dictionary<Guid, string> sessionDeviceMap = new();
    private readonly object sessionLock = new();
    // 不兼容设备集合：旧版协议设备，阻止心跳复活
    private readonly HashSet<string> incompatibleDevices = new();
    private string? localPublicKey;
    private byte[]? localPrivateKey;
    private string? localDeviceId;
    private Timer? heartbeatTimer;
    private readonly TimeSpan heartbeatInterval = TimeSpan.FromSeconds(4);
    private readonly TimeSpan heartbeatTimeout = TimeSpan.FromSeconds(15);
    private readonly Lazy<IRemoteAppService> remoteAppService = new(remoteAppServiceFactory);

    private ObservableCollection<PairedDevice> PairedDevices => deviceManager.PairedDevices;

    /// <summary>
    /// Event fired when a device connection status changes
    /// </summary>
    public event EventHandler<(PairedDevice Device, bool IsConnected)>? ConnectionStatusChanged;

    public async Task<bool> StartServerAsync()
    {
        if (isRunning)
        {
            logger.LogWarning("服务器已在运行");
            return false;
        }
        try
        {
            var localDevice = await deviceManager.GetLocalDeviceAsync();
            localPublicKey = Encoding.UTF8.GetString(localDevice.PublicKey ?? Array.Empty<byte>());
            localPrivateKey = localDevice.PrivateKey;
            localDeviceId = localDevice.DeviceId;

            server = new Server(IPAddress.Any, ServerPort, this, logger)
            {
                OptionReuseAddress = true,
            };

            if (server.Start())
            {
                isRunning = true;
                logger.Info($"服务器已在端口 {ServerPort} 启动");
                StartHeartbeat();
                return true;
            }

            server.Dispose();
            server = null;

            logger.LogError("启动服务器失败");
            return false;
        }
        catch (Exception ex)
        {
            logger.LogError("启动服务器时发生错误：{ex}", ex);
            return false;
        }
    }

    public void SendMessage(string deviceId, string message)
    {
        _ = protocolSender.SendMessageAsync(deviceId, message);
    }

    public void BroadcastMessage(string message)
    {
        try
        {
            var targets = PairedDevices.Where(d => d.ConnectionStatus).Select(d => d.Id).ToList();
            foreach (var deviceId in targets)
            {
                SendMessage(deviceId, message);
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "向所有设备发送消息时出错");
        }
    }

    public void DisconnectDevice(string deviceId)
    {
        if (TryGetSession(deviceId, out var session) && session is not null)
        {
            DisconnectSession(session);
        }
    }

    /// <summary>
    /// 通过UDP心跳包更新设备状态
    /// </summary>
    /// <param name="deviceId">设备ID</param>
    /// <param name="message">UDP心跳包消息内容</param>
    public void UpdateDeviceStatusFromUdp(string deviceId, string? message = null)
    {
        heartbeatProcessor.UpdateDeviceFromUdp(deviceId, message ?? string.Empty, MarkDeviceAlive);
    }

    private bool TryGetSession(string deviceId, out ServerSession? session)
    {
        lock (sessionLock)
        {
            return deviceSessions.TryGetValue(deviceId, out session);
        }
    }

    private List<string> GetConnectedDeviceIds()
    {
        lock (sessionLock)
        {
            return deviceSessions.Keys.ToList();
        }
    }

    private PairedDevice? GetDeviceBySession(ServerSession session)
    {
        string? deviceId = null;
        lock (sessionLock)
        {
            sessionDeviceMap.TryGetValue(session.Id, out deviceId);
        }

        return deviceId is null ? null : PairedDevices.FirstOrDefault(d => d.Id == deviceId);
    }

    private void BindSession(string deviceId, ServerSession session)
    {
        lock (sessionLock)
        {
            if (deviceSessions.TryGetValue(deviceId, out var existing) && existing.Id != session.Id)
            {
                try
                {
                    existing.Disconnect();
                    existing.Dispose();
                }
                catch
                {
                    // best-effort cleanup
                }

                // remove old mapping entry
                sessionDeviceMap.Remove(existing.Id);
            }

            deviceSessions[deviceId] = session;
            sessionDeviceMap[session.Id] = deviceId;
        }
    }

    private void UnbindSession(ServerSession session)
    {
        lock (sessionLock)
        {
            if (sessionDeviceMap.TryGetValue(session.Id, out var deviceId))
            {
                sessionDeviceMap.Remove(session.Id);
                if (deviceSessions.TryGetValue(deviceId, out var existing) && existing.Id == session.Id)
                {
                    deviceSessions.Remove(deviceId);
                }
            }
        }
    }

    private List<(string DeviceId, ServerSession Session)> GetSessionSnapshot()
    {
        lock (sessionLock)
        {
            return deviceSessions.Select(kvp => (kvp.Key, kvp.Value)).ToList();
        }
    }

    private void SendRaw(ServerSession session, string message)
    {
        try
        {
            string messageWithNewline = message + "\n";
            byte[] messageBytes = Encoding.UTF8.GetBytes(messageWithNewline);
            session.Send(messageBytes, 0, messageBytes.Length);
        }
        catch (Exception ex)
        {
            logger.LogError("发送原始消息时出错：{ex}", ex);
        }
    }

    // Server side methods
    public void OnConnected(ServerSession session)
    {

    }

    public void OnDisconnected(ServerSession session)
    {
        sessionBuffers.TryRemove(session.Id, out _);
        UnbindSession(session);
        DetachSession(session);
    }

    public void OnError(SocketError error)
    {
        logger.LogError("Socket 错误：{error}", error);
    }

    public async void OnReceived(ServerSession session, byte[] buffer, long offset, long size)
    {
        try
        {
            string newData = Encoding.UTF8.GetString(buffer, (int)offset, (int)size);

            if (!sessionBuffers.TryGetValue(session.Id, out var bufferedData))
            {
                bufferedData = string.Empty;
            }

            bufferedData += newData;
            while (true)
            {
                int newlineIndex = bufferedData.IndexOf('\n');
                if (newlineIndex == -1)
                {
                    break;
                }

                string message = bufferedData[..newlineIndex].Trim();

                bufferedData = newlineIndex + 1 >= bufferedData.Length
                    ? string.Empty
                    : bufferedData[(newlineIndex + 1)..];

                if (string.IsNullOrEmpty(message)) continue;

                var device = GetDeviceBySession(session);
                await serverLineRouter.RouteLineAsync(session, message, device, this);
            }

            if (string.IsNullOrEmpty(bufferedData))
            {
                sessionBuffers.TryRemove(session.Id, out _);
            }
            else
            {
                sessionBuffers[session.Id] = bufferedData;
            }
        }
        catch (Exception ex)
        {
            logger.LogError("接收会话 {id} 数据时出错：{ex}", session.Id, ex);
            DisconnectSession(session);
        }
    }

    public async Task HandleHandshakeAsync(ServerSession session, string jsonMessage)
    {
        using var doc = System.Text.Json.JsonDocument.Parse(jsonMessage);
        var root = doc.RootElement;
        var remoteDeviceId = root.GetProperty("uuid").GetString() ?? "";
        var remotePublicKey = root.GetProperty("pub_key").GetString() ?? "";
        var remoteIpAddress = root.GetProperty("ip").GetString() ?? "";
        var remoteDeviceType = root.GetProperty("device_type").GetString() ?? "";
        var connectedSessionIpAddress = session.Socket.RemoteEndPoint?.ToString()?.Split(':')[0];
        logger.Info($"收到握手来自 {connectedSessionIpAddress} (类型: {remoteDeviceType})");

        // 检查是否是已知设备，如果是已知设备（重连），则不自动请求应用列表
        bool isKnownDevice = PairedDevices.Any(d => d.Id == remoteDeviceId);

        var device = await deviceManager.VerifyHandshakeAsync(remoteDeviceId, remotePublicKey, string.Empty, connectedSessionIpAddress);

        if (device is not null)
        {
            logger.Info($"设备 {device.Id} 已连接");
            // 如果之前被标记为不兼容，现在成功连接则移除限制
            lock (incompatibleDevices) incompatibleDevices.Remove(device.Id);

            device = await deviceManager.UpdateOrAddDeviceAsync(device, connectedDevice =>
            {
                connectedDevice.ConnectionStatus = true;
                connectedDevice.Session = session;
                connectedDevice.RemotePublicKey = remotePublicKey;
                connectedDevice.SharedSecret ??= NotifyCryptoHelper.GenerateSharedSecretSmart(localPublicKey ?? string.Empty, localPrivateKey, remotePublicKey);
                connectedDevice.RemoteIpAddress = remoteIpAddress;
                connectedDevice.RemoteDeviceType = remoteDeviceType;
                deviceManager.ActiveDevice = connectedDevice;
                connectedDevice.LastHeartbeat = DateTime.UtcNow;

                if (connectedDevice.DeviceSettings.AdbAutoConnect && !string.IsNullOrEmpty(connectedSessionIpAddress))
                {
                    adbService.TryConnectTcp(connectedSessionIpAddress);
                }
            });

            BindSession(device.Id, session);

            if (localDeviceId is not null && localPublicKey is not null)
            {
                var localBattery = systemInfoService.GetSystemBatteryLevel();
                var localIp = NetworkHelper.GetLocalIpAddress() ?? string.Empty;
                var acceptMsg = NativeCore.FormatAccept(localDeviceId, localPublicKey, localIp, localBattery, "pc");
                if (acceptMsg != null)
                {
                    SendRaw(session, acceptMsg);
                }
            }

            var nonNullDevice = device!;
            ConnectionStatusChanged?.Invoke(this, (nonNullDevice, true));

            // 延迟请求应用列表，避免阻塞握手
            // 仅对新配对设备自动触发
            if (!isKnownDevice)
            {
                DelayedRequestAppList(device.Id);
            }
        }
        else
        {
            SendRaw(session, $"REJECT:{localDeviceId ?? string.Empty}");
            await Task.Delay(50);
            logger.Info("设备验证失败或被拒绝");
            DisconnectSession(session);
        }
    }

    /// <summary>
    /// 处理 PAIRING_INIT：接收端（PC）收到发起端（Android）的配对请求。
    /// 协议格式：PAIRING_INIT:<uuid>:<tmpPubKey>:<ipAddress>:<batteryLevel>:<deviceType>
    /// 流程：弹出配对码输入对话框 → 用发起端临时公钥加密配对码 → 回传 PAIRING_RESP
    /// </summary>
    public async Task HandlePairingInitAsync(ServerSession session, string jsonMessage)
    {
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(jsonMessage);
            var root = doc.RootElement;
            var remoteUuid = root.GetProperty("uuid").GetString() ?? "";
            var tmpPubKey = root.GetProperty("tmp_pub_key").GetString() ?? "";
            var remoteIp = root.GetProperty("ip").GetString();
            if (string.IsNullOrEmpty(remoteIp))
                remoteIp = session.Socket.RemoteEndPoint?.ToString()?.Split(':')[0] ?? string.Empty;

            // 检查是否已被拒绝
            if (deviceManager.PairedDevices.Any(d => d.Id == remoteUuid))
            {
                logger.LogWarning("设备已配对，忽略 PAIRING_INIT: {uuid}", remoteUuid);
                SendRaw(session, $"REJECT:{localDeviceId ?? string.Empty}");
                DisconnectSession(session);
                return;
            }

            logger.Info($"收到 PAIRING_INIT: {remoteUuid}");

            // 在主线程上显示配对码输入对话框
            string? pairingCode = null;
            // 尝试从已发现设备中获取设备名
            var discoveredName = PairedDevices.FirstOrDefault(d => d.Id == remoteUuid)?.Name
                ?? Ioc.Default.GetService<IDiscoveryService>()?.DiscoveredDevices
                    .FirstOrDefault(d => d.DeviceId == remoteUuid)?.DeviceName
                ?? remoteUuid;
            await App.MainWindow.DispatcherQueue.EnqueueAsync(async () =>
            {
                var dialog = new Dialogs.PairingCodeDialog(discoveredName)
                {
                    XamlRoot = App.MainWindow.Content!.XamlRoot
                };
                var result = await dialog.ShowAsync(ContentDialogPlacement.Popup);
                if (result == ContentDialogResult.Primary)
                {
                    pairingCode = dialog.PairingCode;
                }
            });

            // 用户取消了输入
            if (pairingCode == null)
            {
                logger.Info($"用户取消了配对: {remoteUuid}");
                SendRaw(session, $"REJECT:{localDeviceId ?? string.Empty}");
                DisconnectSession(session);
                return;
            }

            // 使用发起端的临时公钥加密配对码（ECIES 风格）
            try
            {
                // 1. 生成接收端（PC）的临时密钥对
                var receiverTmpKeyPair = EcdhHelper.GenerateEphemeralKeyPair();
                var receiverTmpPubKeyB64 = EcdhHelper.GetPublicKeyBase64(receiverTmpKeyPair);

                // 2. ECDH 密钥协商：用 PC 临时私钥 + Android 临时公钥（原始 ECDH，不含 HKDF）
                var privateKeyBytes = EcdhHelper.SerializePrivateKey(receiverTmpKeyPair);
                var rawSharedSecret = EcdhHelper.DeriveRawEcdh(privateKeyBytes, tmpPubKey);

                // 3. 派生 AES 密钥并加密配对码（与 Android 端 EncryptionManager.hkdfDeriveKey + encrypt 兼容）
                var aesKeyBytes = Convert.FromBase64String(EcdhHelper.DeriveAesKey(rawSharedSecret));
                var encryptedCode = NotifyCryptoHelper.Encrypt(pairingCode, aesKeyBytes);

                // 4. 获取 PC 的长期 ECDH 公钥
                var localDevice = await deviceManager.GetLocalDeviceAsync();
                var ltPubKey = Encoding.UTF8.GetString(localDevice.PublicKey ?? Array.Empty<byte>());

                // 5. 关闭 PAIRING_INIT session（不回传任何内容），
                //    新建 TCP 连接发送 PAIRING_RESP 到 Android 服务器
                DisconnectSession(session);

                var pairingResp = NativeCore.FormatPairingResp(localDeviceId, receiverTmpPubKeyB64, ltPubKey, encryptedCode, remoteIp, systemInfoService.GetSystemBatteryLevel(), "pc");
                if (pairingResp == null) return;
                
                using var androidSocket = new System.Net.Sockets.TcpClient();
                 await androidSocket.ConnectAsync(System.Net.IPAddress.Parse(remoteIp), ServerPort);
                await using var stream = androidSocket.GetStream();
                var respBytes = Encoding.UTF8.GetBytes(pairingResp + "\n");
                await stream.WriteAsync(respBytes);
                await stream.FlushAsync();

                // 6. 读取 ACCEPT 响应
                using var reader = new StreamReader(stream, Encoding.UTF8);
                var acceptLine = await reader.ReadLineAsync();
                
                var acceptJson = NativeCore.DecodeLine(acceptLine ?? "");
                if (acceptJson != null)
                {
                    using var acceptDoc = System.Text.Json.JsonDocument.Parse(acceptJson);
                    var acceptRoot = acceptDoc.RootElement;
                    if (acceptRoot.TryGetProperty("header", out var hdrProp) && hdrProp.GetString() == "ACCEPT")
                    {
                        var initiatorLtPubKey = acceptRoot.GetProperty("lt_pub_key").GetString() ?? "";
                        var acceptIp = acceptRoot.TryGetProperty("ip", out var ipProp) ? ipProp.GetString() ?? string.Empty : string.Empty;
                        var acceptDeviceType = acceptRoot.TryGetProperty("device_type", out var dtProp) ? dtProp.GetString() ?? "unknown" : "unknown";

                        logger.Info($"收到 ACCEPT，配对码验证通过: {remoteUuid}");

                        // 完成 ECDH 标准密钥交换并保存设备（用户已通过输入配对码确认，跳过弹窗）
                        var localKeyDevice = await deviceManager.GetLocalDeviceAsync();
                        var localKey = Encoding.UTF8.GetString(localKeyDevice.PublicKey ?? Array.Empty<byte>());
                        var sharedSecretBytes = NotifyCryptoHelper.GenerateSharedSecretSmart(
                            localKey, localKeyDevice.PrivateKey, initiatorLtPubKey);

                        var pairedDevice = new PairedDevice(remoteUuid)
                         {
                             Name = discoveredName,
                             RemotePublicKey = initiatorLtPubKey,
                             SharedSecret = sharedSecretBytes,
                             RemoteIpAddress = remoteIp,
                             RemoteDeviceType = acceptDeviceType,
                             LastHeartbeat = DateTime.UtcNow,
                         };
                         await deviceManager.UpdateOrAddDeviceAsync(pairedDevice);
                         NativeCore.MigrateSharedSecret(remoteUuid, sharedSecretBytes);
                          Ioc.Default.GetRequiredService<DeviceRepository>().AddOrUpdateRemoteDevice(new RemoteDeviceEntity
                         {
                             DeviceId = remoteUuid,
                             Name = discoveredName ?? remoteUuid,
                             SharedSecret = sharedSecretBytes,
                             PublicKey = initiatorLtPubKey,
                             IpAddresses = string.IsNullOrEmpty(remoteIp) ? [] : [remoteIp],
                             LastConnected = DateTime.UtcNow,
                         });
                         ConnectionStatusChanged?.Invoke(this, (pairedDevice, false));
                         logger.Info($"配对完成: {remoteUuid}");

                         if (!PairedDevices.Any(d => d.Id == remoteUuid))
                         {
                             DelayedRequestAppList(remoteUuid);
                         }
                    }
                }
                else
                {
                    logger.Warn($"未收到 ACCEPT 或配对被拒绝: {acceptLine}");
                }
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "加密配对码失败: {uuid}", remoteUuid);
                SendRaw(session, $"REJECT:{localDeviceId ?? string.Empty}");
                DisconnectSession(session);
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "处理 PAIRING_INIT 异常");
            DisconnectSession(session);
        }
    }

    /// <summary>
    /// 处理 PAIRING_RESP：PC 作为发起端时收到接收端回传的加密配对码。
    /// 协议格式：PAIRING_RESP:<uuid_R>:<tmpPub_R>:<ltPub_R>:<encryptedCode>:<ip>:<battery>:<deviceType>
    /// 注意：当前 PC 通常作为接收端，此方法主要用于未来扩展或 PC-PC 配对。
    /// </summary>
    public async Task HandlePairingRespAsync(ServerSession session, string jsonMessage)
    {
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(jsonMessage);
            var root = doc.RootElement;
            var remoteUuid = root.GetProperty("uuid").GetString() ?? "";

            logger.LogWarning("收到 PAIRING_RESP，但 PC 当前不作为配对发起端。忽略: {uuid}", remoteUuid);
            SendRaw(session, $"REJECT:{localDeviceId ?? string.Empty}");
            DisconnectSession(session);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "处理 PAIRING_RESP 异常");
            DisconnectSession(session);
        }
    }

    /// <summary>
    /// 处理 ACCEPT：Android 端验证配对码通过后回传的确认消息。
    /// </summary>
    public async Task HandlePairingAcceptAsync(ServerSession session, string jsonMessage)
    {
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(jsonMessage);
            var root = doc.RootElement;
            var remoteUuid = root.GetProperty("uuid").GetString() ?? "";
            var remoteLtPubKey = root.GetProperty("lt_pub_key").GetString() ?? "";
            var remoteIp = root.GetProperty("ip").GetString() ?? string.Empty;
            var remoteDeviceType = root.GetProperty("device_type").GetString() ?? "unknown";
            var connectedSessionIpAddress = session.Socket.RemoteEndPoint?.ToString()?.Split(':')[0];

            logger.Info($"收到 ACCEPT，配对码验证通过: {remoteUuid}");

            // 完成 ECDH 标准密钥交换并保存设备
            var device = await deviceManager.VerifyHandshakeAsync(remoteUuid, remoteLtPubKey, null, connectedSessionIpAddress);
            if (device != null)
            {
                bool isKnownDevice = PairedDevices.Any(d => d.Id == device.Id);

                device = await deviceManager.UpdateOrAddDeviceAsync(device, UpdateDeviceConfig =>
                {
                    UpdateDeviceConfig.ConnectionStatus = true;
                    UpdateDeviceConfig.Session = session;
                    UpdateDeviceConfig.RemotePublicKey = remoteLtPubKey;
                    UpdateDeviceConfig.SharedSecret ??= NotifyCryptoHelper.GenerateSharedSecretSmart(
                        localPublicKey ?? string.Empty, localPrivateKey, remoteLtPubKey);
                    UpdateDeviceConfig.RemoteIpAddress = remoteIp;
                    UpdateDeviceConfig.RemoteDeviceType = remoteDeviceType;
                    deviceManager.ActiveDevice = UpdateDeviceConfig;
                    UpdateDeviceConfig.LastHeartbeat = DateTime.UtcNow;
                });

                BindSession(device.Id, session);
                ConnectionStatusChanged?.Invoke(this, (device, true));
                logger.Info($"配对完成: {remoteUuid}");

                if (!isKnownDevice)
                {
                    DelayedRequestAppList(device.Id);
                }
            }
            else
            {
                logger.Warn($"ACCEPT 处理失败，设备验证未通过: {remoteUuid}");
                DisconnectSession(session);
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "处理 ACCEPT 异常");
            DisconnectSession(session);
        }
    }

    private async Task HandlePairingRequestAsync(ServerSession session, string message, string connectedSessionIpAddress)
    {
        try
        {
            var parts = message.Split(':');
            if (parts.Length < 7)
            {
                SendRaw(session, "REJECT:");
                DisconnectSession(session);
                return;
            }

            var remoteDeviceId = parts[1];
            var remotePublicKey = parts[2];
            var pairingCode = parts[3];
            var remoteIpAddress = parts.Length > 4 ? parts[4] : connectedSessionIpAddress;
            var remoteDeviceType = parts.Length > 6 ? parts[6] : "unknown";

            // 验证配对码
            if (!PairingCodeHelper.VerifyCode(pairingCode))
            {
                logger.Warn($"配对码验证失败: {remoteDeviceId}");
                SendRaw(session, $"REJECT:{localDeviceId}");
                DisconnectSession(session);
                return;
            }

            logger.Info($"配对码验证通过: {remoteDeviceId}");

            // 获取或创建设备
            var device = await deviceManager.VerifyHandshakeAsync(remoteDeviceId, remotePublicKey, null, connectedSessionIpAddress);
            if (device != null)
            {
                bool isKnownDevice = PairedDevices.Any(d => d.Id == device.Id);

                device = await deviceManager.UpdateOrAddDeviceAsync(device, connectedDevice =>
                {
                    connectedDevice.ConnectionStatus = true;
                    connectedDevice.Session = session;
                    connectedDevice.RemotePublicKey = remotePublicKey;
                    connectedDevice.SharedSecret ??= NotifyCryptoHelper.GenerateSharedSecretSmart(
                        localPublicKey ?? string.Empty, localPrivateKey, remotePublicKey);
                    connectedDevice.RemoteIpAddress = remoteIpAddress;
                    connectedDevice.RemoteDeviceType = remoteDeviceType;
                    deviceManager.ActiveDevice = connectedDevice;
                    connectedDevice.LastHeartbeat = DateTime.UtcNow;
                });

                BindSession(device.Id, session);

                if (localDeviceId != null && localPublicKey != null)
                {
                    var localBattery = systemInfoService.GetSystemBatteryLevel();
                    var localIp = NetworkHelper.GetLocalIpAddress() ?? string.Empty;
                    var acceptMsg = NativeCore.FormatAccept(localDeviceId, localPublicKey, localIp, localBattery, "pc");
                    if (acceptMsg != null)
                    {
                        SendRaw(session, acceptMsg);
                    }
                }

                ConnectionStatusChanged?.Invoke(this, (device, true));

                if (!isKnownDevice)
                {
                    DelayedRequestAppList(device.Id);
                }
            }
            else
            {
                SendRaw(session, $"REJECT:{localDeviceId ?? string.Empty}");
                DisconnectSession(session);
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "处理配对请求异常");
            SendRaw(session, $"REJECT:{localDeviceId ?? string.Empty}");
            DisconnectSession(session);
        }
    }

    public async Task ProcessProtocolMessageAsync(PairedDevice device, string message)
    {
        try
        {
            var jsonStr = NativeCore.DecodeLine(message);
            if (jsonStr == null)
            {
                if (heartbeatProcessor.TryProcessHeartbeat(message, device, MarkDeviceAlive))
                {
                    return;
                }

                logger.LogDebug("收到不支持的消息类型: {message}", message.Length > 50 ? message[..50] + "..." : message);
                return;
            }

            using var doc = System.Text.Json.JsonDocument.Parse(jsonStr);
            var root = doc.RootElement;
            var header = root.GetProperty("header").GetString();

            if (root.TryGetProperty("type", out var typeProp) && typeProp.GetString() == "data")
            {
                var plaintext = root.GetProperty("plaintext").GetString() ?? "";
                await ProcessDecryptedDataAsync(device, header ?? "", plaintext);
                return;
            }

            if (header == "HEARTBEAT_TCP")
            {
                heartbeatProcessor.TryProcessHeartbeat(message, device, MarkDeviceAlive);
                return;
            }

            logger.LogDebug("收到已认证设备发送的协议消息: {header}", header);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "处理协议消息时出错");
        }
    }

    /// <summary>
    /// 处理DATA_*加密业务消息
    /// </summary>
    /// <param name="device">设备</param>
    /// <param name="message">完整消息</param>
    private async Task ProcessDataMessageAsync(PairedDevice device, string message)
    {
        // 更新设备活跃时间
        MarkDeviceAlive(device);

        // 使用协议路由器处理消息
        await protocolRouter.ProcessDataMessageAsync(device, message);
    }

    /// <summary>
    /// 处理已由 Rust 解密后的 DATA_* 业务消息。
    /// </summary>
    public async Task ProcessDecryptedDataAsync(PairedDevice device, string header, string plaintext)
    {
        MarkDeviceAlive(device);
        await protocolRouter.ProcessDecryptedDataAsync(device, header, plaintext);
    }

    public async Task<PairedDevice?> TryAttachExistingDeviceSessionAsync(ServerSession session, string message)
    {
        try
        {
            string remoteDeviceId;
            string remotePublicKey = string.Empty;

            // 处理其他格式
            var parts = message.Split(':');
            if (parts.Length < 3) return null;
            remoteDeviceId = parts[1];
            remotePublicKey = parts[2];

            var device = PairedDevices.FirstOrDefault(d => d.Id == remoteDeviceId);
            if (device is null) return null;

            if (device.RemotePublicKey is not null && !string.Equals(device.RemotePublicKey, remotePublicKey, StringComparison.Ordinal))
            {
                logger.LogWarning("设备 {id} 的远端公钥不匹配", remoteDeviceId);
                return null;
            }

            if (localPublicKey is null)
            {
                logger.LogWarning("本地公钥未初始化，无法绑定会话");
                return null;
            }

            // 获取会话的远程IP地址
            var connectedSessionIpAddress = session.Socket.RemoteEndPoint?.ToString()?.Split(':')[0];

            await App.MainWindow.DispatcherQueue.EnqueueAsync(() =>
            {
                device.Session = session;
                device.ConnectionStatus = true;
                device.RemotePublicKey = remotePublicKey;
                var deviceRepo = Ioc.Default.GetRequiredService<DeviceRepository>();
                if (device.SharedSecret == null)
                {
                    device.SharedSecret = NotifyCryptoHelper.GenerateSharedSecretSmart(localPublicKey, localPrivateKey, remotePublicKey);
                    if (device.SharedSecret != null)
                    {
                        NativeCore.MigrateSharedSecret(remoteDeviceId, device.SharedSecret);
                        if (deviceRepo.HasDevice(remoteDeviceId, out var dbDevice))
                        {
                            dbDevice.SharedSecret = device.SharedSecret;
                            deviceRepo.AddOrUpdateRemoteDevice(dbDevice);
                        }
                    }
                }
                if (deviceRepo.HasDevice(remoteDeviceId, out var dbEntity))
                {
                    dbEntity.PublicKey = remotePublicKey;
                    deviceRepo.AddOrUpdateRemoteDevice(dbEntity);
                }
                device.LastHeartbeat = DateTime.UtcNow;
                deviceManager.ActiveDevice ??= device;

                // 更新设备IP地址
                if (!string.IsNullOrEmpty(connectedSessionIpAddress))
                {
                    // 确保IP地址列表存在
                    if (device.IpAddresses == null)
                    {
                        device.IpAddresses = new List<string>();
                    }

                    // 如果IP地址不存在，添加到列表中
                    if (!device.IpAddresses.Contains(connectedSessionIpAddress))
                    {
                        logger.LogInformation("添加设备 {deviceName} 的IP地址：{newIp}", device.Name, connectedSessionIpAddress);
                        logger.LogInformation("会话远程IP地址：{ipAddress}", connectedSessionIpAddress);

                        // 添加新的IP地址到列表中，保留旧的IP地址
                        device.IpAddresses.Add(connectedSessionIpAddress);
                    }

                    // 持久化 IP 地址到数据库
                    var ipRepo = Ioc.Default.GetRequiredService<DeviceRepository>();
                    if (ipRepo.HasDevice(device.Id, out var ipEntity))
                    {
                        ipEntity.IpAddresses = device.IpAddresses;
                        ipRepo.AddOrUpdateRemoteDevice(ipEntity);
                    }
                }
            });

            BindSession(device.Id, session);

            ConnectionStatusChanged?.Invoke(this, (device, true));

            return device;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "绑定预握手 DATA 会话时出错");
            return null;
        }
    }

    private void DelayedRequestAppList(string deviceId)
    {
        Task.Run(async () =>
        {
            try
            {
                // 等待几秒钟，确保连接稳定，且不阻塞主握手流程
                await Task.Delay(3000);

                remoteAppService.Value.SendAppListRequest(deviceId);
                logger.LogDebug("已自动触发设备 {deviceId} 的应用列表请求", deviceId);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "自动请求应用列表失败");
            }
        });
    }

    public void DisconnectSession(ServerSession session)
    {
        try
        {
            sessionBuffers.TryRemove(session.Id, out _);
            UnbindSession(session);
            session.Disconnect();
            session.Dispose();
            DetachSession(session);
        }
        catch (Exception ex)
        {
            logger.Error($"断开连接时出错：{ex.Message}");
        }
    }

    private void DetachSession(ServerSession session)
    {
        var device = GetDeviceBySession(session) ?? PairedDevices.FirstOrDefault(d => d.Session == session);
        if (device is null) return;

        UpdateDeviceState(device, d =>
        {
            d.Session = null;
            // 不要在TCP会话断开时立即标记为离线，由心跳超时决定
            logger.LogTrace($"设备 {d.Name} 的会话已解绑");
        });
    }

    private void MarkDeviceAlive(PairedDevice device)
    {
        // 不兼容的旧版设备，不复活
        lock (incompatibleDevices)
        {
            if (incompatibleDevices.Contains(device.Id)) return;
        }
        var now = DateTime.UtcNow;
        UpdateDeviceState(device, d =>
        {
            d.LastHeartbeat = now;
            if (!d.ConnectionStatus)
            {
                d.ConnectionStatus = true;
                ConnectionStatusChanged?.Invoke(this, (d, true));
            }
        });
    }



    private void StartHeartbeat()
    {
        heartbeatTimer ??= new Timer(_ => HeartbeatTick(), null, heartbeatInterval, heartbeatInterval);
    }

    private void HeartbeatTick()
    {
        try
        {
            if (localDeviceId is null) return;

            var batteryLevel = systemInfoService.GetSystemBatteryLevel();
            var isCharging = systemInfoService.GetSystemChargingStatus();
            var signedBattery = isCharging ? Math.Abs(batteryLevel) : -Math.Abs(batteryLevel);

            var localDevice = deviceManager.GetLocalDeviceAsync().Result;
            var deviceName = localDevice?.DeviceName ?? "PC";
            var encodedName = Convert.ToBase64String(Encoding.UTF8.GetBytes(deviceName));

            var payload = NativeCore.FormatHeartbeat(localDeviceId, encodedName, (ushort)ServerPort, signedBattery, "pc");
            if (payload == null) return;
            var bytes = Encoding.UTF8.GetBytes(payload);
            const int udpPort = 23334; // 使用与Android端相同的UDP端口

            // 使用UDP广播发送心跳
            using var udpClient = new System.Net.Sockets.UdpClient();
            udpClient.EnableBroadcast = true;

            // 获取本地网络的广播地址
            var localAddresses = NetworkHelper.GetAllValidAddresses();
            var broadcastEndpoints = localAddresses.Select(ipInfo =>
            {
                var network = new Data.Models.IPNetwork(ipInfo.Address, ipInfo.SubnetMask);
                var broadcastAddress = network.BroadcastAddress;
                return new IPEndPoint(broadcastAddress, udpPort);
            }).Distinct().ToList();

            // 添加全局广播地址
            broadcastEndpoints.Add(new IPEndPoint(IPAddress.Broadcast, udpPort));

            // 发送广播心跳
            foreach (var endPoint in broadcastEndpoints)
            {
                try
                {
                    udpClient.Send(bytes, bytes.Length, endPoint);
                }
                catch
                {
                    // best-effort UDP heartbeat send
                }
            }

            foreach (var device in PairedDevices.ToList())
            {
                var last = device.LastHeartbeat;
                if (last.HasValue && DateTime.UtcNow - last.Value > heartbeatTimeout && device.ConnectionStatus)
                {
                    UpdateDeviceState(device, d =>
                    {
                        d.ConnectionStatus = false;
                        d.Session = null;
                        if (TryGetSession(d.Id, out var staleSession) && staleSession is not null)
                        {
                            UnbindSession(staleSession);
                        }
                        ConnectionStatusChanged?.Invoke(this, (d, false));
                    });
                }
            }
        }
        catch
        {
            // best-effort heartbeat
        }
    }

    private void UpdateDeviceState(PairedDevice device, Action<PairedDevice> update)
    {
        var dispatcher = App.MainWindow?.DispatcherQueue;

        if (dispatcher is null)
        {
            update(device);
            return;
        }

        if (dispatcher.HasThreadAccess)
        {
            update(device);
            return;
        }

        dispatcher.TryEnqueue(() => update(device));
    }

    /// <summary>
    /// 显示非阻塞系统通知，提示设备需要升级
    /// </summary>
    private static void ShowUpgradeToast(string deviceName)
    {
        try
        {
            var template = ToastNotificationManager.GetTemplateContent(ToastTemplateType.ToastText02);
            var elements = template.GetElementsByTagName("text");
            elements[0].AppendChild(template.CreateTextNode("设备协议不兼容"));
            elements[1].AppendChild(template.CreateTextNode($"设备「{deviceName}」使用旧版加密协议，已被拒绝连接。请升级该设备上的 NotifyRelay。"));
            var toast = new ToastNotification(template);
            ToastNotificationManager.CreateToastNotifier().Show(toast);
        }
        catch
        {
            // Toast 通知可能因系统权限或上下文不可用而失败，静默忽略
        }
    }
}