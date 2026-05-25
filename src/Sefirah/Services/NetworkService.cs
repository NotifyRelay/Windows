using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text;
using CommunityToolkit.WinUI;
using NotifyRelay.Data.Contracts;
using NotifyRelay.Data.Models;
using NotifyRelay.Helpers;
using NotifyRelay.Services.Socket;
using Uno.Logging;


namespace NotifyRelay.Services;

public class NetworkService(
    ILogger<NetworkService> logger,
    IDeviceManager deviceManager,
    IAdbService adbService,
    IScreenMirrorService screenMirrorService,
    ISystemInfoService systemInfoService,
    ProtocolRouter protocolRouter,
    Func<IRemoteAppService> remoteAppServiceFactory) : INetworkService, ISessionManager, ITcpServerProvider
{
    private Server? server;
    public int ServerPort { get; private set; } = 23333;
    private bool isRunning;


    private readonly ConcurrentDictionary<Guid, string> sessionBuffers = new();
    private readonly Dictionary<string, ServerSession> deviceSessions = new();
    private readonly Dictionary<Guid, string> sessionDeviceMap = new();
    private readonly object sessionLock = new();
    private string? localPublicKey;
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
        _ = ProtocolSender.SendMessageAsync(logger, deviceManager, deviceId, message);
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
    public void UpdateDeviceStatusFromUdp(string deviceId, string message = null)
    {
        var device = PairedDevices.FirstOrDefault(d => d.Id == deviceId);
        if (device != null)
        {
            // logger.LogInformation("通过UDP心跳包更新设备状态：{0} ({1})", device.Name, device.Id);

            // 解析UDP心跳包中的电量信息
            if (!string.IsNullOrEmpty(message))
            {
                var parts = message.Split(':');
                if (parts.Length >= 5)
                {
                    try
                    {
                        // 解析充电状态和电量
                        var batteryPart = parts[3];
                        var chargeSign = batteryPart[0];
                        var isCharging = chargeSign == '+';
                        var batteryLevelStr = batteryPart.Substring(1);
                        var batteryLevel = int.TryParse(batteryLevelStr, out var parsedBattery) ? Math.Clamp(parsedBattery, 0, 100) : 0;

                        // 更新设备状态
                        var deviceStatus = new DeviceStatus
                        {
                            BatteryStatus = batteryLevel,
                            ChargingStatus = isCharging
                        };

                        // 调用设备管理器更新设备状态
                        deviceManager.UpdateDeviceStatus(device, deviceStatus);
                        // logger.LogDebug("UDP心跳包更新设备电量：{0} ({1})，电量={2}%，充电={3}", device.Name, device.Id, batteryLevel, isCharging);
                    }
                    catch (Exception ex)
                    {
                        logger.LogWarning("解析UDP心跳包电量信息失败：{0}", ex);
                    }
                }
            }

            MarkDeviceAlive(device);
        }
        else
        {
            // logger.LogDebug("UDP心跳包未找到已配对设备：{0}", deviceId);
        }
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
                if (device is null)
                {
                    await HandleHandshakeAsync(session, message);
                }
                else
                {
                    await ProcessProtocolMessageAsync(device, message);
                }
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

    private async Task HandleHandshakeAsync(ServerSession session, string message)
    {
        if (!message.StartsWith("HANDSHAKE:"))
        {
            if (message.StartsWith("DATA_"))
            {
                var attachedDevice = await TryAttachExistingDeviceSessionAsync(session, message);
                if (attachedDevice is not null)
                {
                    await ProcessProtocolMessageAsync(attachedDevice, message);
                    return;
                }
            }

            logger.LogWarning("收到意外的预握手消息，来源：{id}，消息：{message}", session.Id, message);
            return;
        }

        var parts = message.Split(':');
        if (parts.Length < 6)
        {
            logger.LogWarning("握手格式无效，期望至少6个部分");
            SendRaw(session, $"REJECT:{localDeviceId ?? string.Empty}");
            DisconnectSession(session);
            return;
        }

        var remoteDeviceId = parts[1];
        var remotePublicKey = parts[2];
        var remoteIpAddress = parts[3];
        var remoteBattery = parts[4];
        var remoteDeviceType = parts[5];
        var discoveredName = PairedDevices.FirstOrDefault(d => d.Id == remoteDeviceId)?.Name;

        if (discoveredName is null)
        {
            var discovery = Ioc.Default.GetService<IDiscoveryService>();
            discoveredName = discovery?.DiscoveredDevices.FirstOrDefault(d => d.DeviceId == remoteDeviceId)?.DeviceName;
        }
        var connectedSessionIpAddress = session.Socket.RemoteEndPoint?.ToString()?.Split(':')[0];
        logger.Info($"收到握手来自 {connectedSessionIpAddress} (类型: {remoteDeviceType}, 电量: {remoteBattery})");

        // 检查是否是已知设备，如果是已知设备（重连），则不自动请求应用列表
        bool isKnownDevice = PairedDevices.Any(d => d.Id == remoteDeviceId);

        var device = await deviceManager.VerifyHandshakeAsync(remoteDeviceId, remotePublicKey, discoveredName, connectedSessionIpAddress);

        if (device is not null)
        {
            logger.Info($"设备 {device.Id} 已连接");

            device = await deviceManager.UpdateOrAddDeviceAsync(device, connectedDevice =>
            {
                connectedDevice.ConnectionStatus = true;
                connectedDevice.Session = session;
                connectedDevice.RemotePublicKey = remotePublicKey;
                connectedDevice.SharedSecret ??= NotifyCryptoHelper.GenerateSharedSecretBytes(localPublicKey ?? string.Empty, remotePublicKey);
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
                var localBattery = systemInfoService.GetSystemBatteryLevel() > 100 ? "100+" : systemInfoService.GetSystemBatteryLevel().ToString();
                var localIp = NetworkHelper.GetLocalIpAddress();
                SendRaw(session, $"ACCEPT:{localDeviceId}:{localPublicKey}:{localIp}:{localBattery}:pc");
            }

            ConnectionStatusChanged?.Invoke(this, (device, true));

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



    public async Task ProcessProtocolMessageAsync(PairedDevice device, string message)
    {
        try
        {
            // 处理心跳包（新格式）
            var parts = message.Split(':');
            if (parts.Length >= 5)
            {
                // 获取心跳包中的设备ID
                var heartbeatDeviceId = parts[0];

                // 查找与心跳包设备ID匹配的设备
                var targetDevice = PairedDevices.FirstOrDefault(d => d.Id == heartbeatDeviceId);

                if (targetDevice != null)
                {
                    // 心跳格式：<uuid>:<displayName>:<port>:<+/-><batteryLevel>:<deviceType>
                    try
                    {
                        // 解析充电状态和电量
                        var batteryPart = parts[3];
                        var chargeSign = batteryPart[0];
                        var isCharging = chargeSign == '+';
                        var batteryLevelStr = batteryPart.Substring(1);
                        var batteryLevel = int.TryParse(batteryLevelStr, out var parsedBattery) ? Math.Clamp(parsedBattery, 0, 100) : 0;

                        // 更新设备状态
                        var deviceStatus = new DeviceStatus
                        {
                            BatteryStatus = batteryLevel,
                            ChargingStatus = isCharging
                        };

                        // 调用设备管理器更新设备状态
                        deviceManager.UpdateDeviceStatus(targetDevice, deviceStatus);
                    }
                    catch (Exception ex)
                    {
                        logger.LogWarning("解析心跳包失败：{ex}", ex);
                    }

                    MarkDeviceAlive(targetDevice);
                    return;
                }
            }

            if (message.StartsWith("DATA_"))
            {
                // 处理DATA_*加密业务消息
                await ProcessDataMessageAsync(device, message);
                return;
            }

            logger.LogDebug("收到不支持的消息类型，按照要求直接不处理: {message}", message.Length > 50 ? message[..50] + "..." : message);
            return;
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

    private async Task<PairedDevice?> TryAttachExistingDeviceSessionAsync(ServerSession session, string message)
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
                device.SharedSecret ??= NotifyCryptoHelper.GenerateSharedSecretBytes(localPublicKey, remotePublicKey);
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

    private void DisconnectSession(ServerSession session)
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

            // 获取PC设备电量百分比和充电状态
            var batteryLevel = systemInfoService.GetSystemBatteryLevel(); // 接入系统电量API
            var isCharging = systemInfoService.GetSystemChargingStatus(); // 获取充电状态
            var chargeSign = isCharging ? "+" : "-";

            // 获取本地设备名称
            var localDevice = deviceManager.GetLocalDeviceAsync().Result;
            var deviceName = localDevice?.DeviceName ?? "PC";
            var encodedName = Convert.ToBase64String(Encoding.UTF8.GetBytes(deviceName));

            // 心跳格式：<uuid>:<displayName>:<port>:<+/-><batteryLevel>:<deviceType>
            var payload = $"{localDeviceId}:{encodedName}:{ServerPort}:{chargeSign}{batteryLevel}:pc";
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
}