using System.Net.Sockets;
using System.Text;
using NotifyRelay.Data.Contracts;
using NotifyRelay.Data.Models;
using NotifyRelay.Native;

namespace NotifyRelay.Services;

/// <summary>
/// 统一加密发送器
///
/// 封装加密、认证检查、TCP发送与报文头拼装：
/// 最终报文格式：`<HEADER>:<localUuid>:<localPublicKey>:<encryptedPayload>\n`
/// </summary>
public class ProtocolSender : IProtocolSender
{
    private const string TAG = "ProtocolSender";
    private const int DEFAULT_TIMEOUT = 80000;
    private const int DEFAULT_CONNECT_TIMEOUT = 5000;

    private readonly ILogger<ProtocolSender> _logger;
    private readonly IDeviceManager _deviceManager;

    public ProtocolSender(
        ILogger<ProtocolSender> logger,
        IDeviceManager deviceManager)
    {
        _logger = logger;
        _deviceManager = deviceManager;
    }

    /// <summary>
    /// 发送消息，自动从JSON中提取type作为协议头
    /// </summary>
    /// <param name="deviceId">目标设备ID</param>
    /// <param name="messageJson">消息JSON字符串</param>
    public async Task SendMessageAsync(string deviceId, string messageJson)
    {
        string header = "DATA_JSON";
        try
        {
            using var doc = JsonDocument.Parse(messageJson);
            if (doc.RootElement.TryGetProperty("type", out var typeProp))
            {
                header = typeProp.GetString() ?? "DATA_JSON";
            }
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "解析消息JSON以提取type失败，使用默认头DATA_JSON");
        }

        await SendMessageAsync(deviceId, messageJson, header);
    }

    /// <summary>
    /// 发送消息，指定协议头
    /// </summary>
    /// <param name="deviceId">目标设备ID</param>
    /// <param name="messageJson">消息JSON字符串</param>
    /// <param name="header">协议头</param>
    public async Task SendMessageAsync(string deviceId, string messageJson, string header)
    {
        var device = _deviceManager.PairedDevices.FirstOrDefault(d => d.Id == deviceId);
        if (device is null)
        {
            _logger.LogWarning("跳过发送：未找到设备 {deviceId}", deviceId);
            return;
        }

        var localDevice = await _deviceManager.GetLocalDeviceAsync();
        var localDeviceId = localDevice.DeviceId;
        var localPublicKey = Encoding.UTF8.GetString(localDevice.PublicKey ?? Array.Empty<byte>());

        if (localPublicKey is null || localDeviceId is null)
        {
            _logger.LogWarning("本地身份未初始化，跳过发送");
            return;
        }

        await SendEncryptedAsync(device, header, messageJson, localDeviceId, localPublicKey);
    }

    /// <summary>
    /// 发送一条加密负载到指定设备。
    /// </summary>
    /// <param name="device">目标设备</param>
    /// <param name="header">消息头，例如：DATA_JSON / DATA_ICON_REQUEST / DATA_ICON_RESPONSE 等</param>
    /// <param name="plaintext">明文内容</param>
    /// <param name="localDeviceId">本地设备ID</param>
    /// <param name="localPublicKey">本地公钥</param>
    /// <param name="timeoutMs">超时时间</param>
    public async Task SendEncryptedAsync(
        PairedDevice device,
        string header,
        string plaintext,
        string localDeviceId,
        string localPublicKey,
        int timeoutMs = DEFAULT_TIMEOUT
    )
    {
        try
        {
            if (device.SharedSecret == null)
            {
                _logger.LogWarning("设备未认证或未接受：{deviceName}", device.Name);
                return;
            }

            if (device.IpAddresses == null || device.IpAddresses.Count == 0)
            {
                _logger.LogWarning("设备 {deviceName} 没有可用的IP地址", device.Name);
                return;
            }

            const int notifyRelayPort = 23333;

            string? framedMessage = NativeCore.EncryptMessage(header, localDeviceId, localPublicKey, device.Id, plaintext);
            if (framedMessage == null)
            {
                _logger.LogError("Rust加密失败: EncryptMessage, device={deviceName}", device.Name);
                return;
            }
            byte[] messageBytes = Encoding.UTF8.GetBytes(framedMessage + "\n");

            _logger.LogDebug("消息字节长度：{length}", messageBytes.Length);

            var ipAddressesCopy = device.IpAddresses.ToList();

            foreach (string ipAddress in ipAddressesCopy)
            {
                _logger.LogInformation("发送到设备：{deviceName} ({ipAddress})", device.Name, ipAddress);

                using var tcpClient = new TcpClient();
                tcpClient.ReceiveTimeout = (int)timeoutMs;
                tcpClient.SendTimeout = (int)timeoutMs;

                try
                {
                    var connectTask = tcpClient.ConnectAsync(ipAddress, notifyRelayPort);
                    var delayTask = Task.Delay(DEFAULT_CONNECT_TIMEOUT);
                    var connectResult = await Task.WhenAny(connectTask, delayTask);

                    if (connectResult == delayTask)
                    {
                        tcpClient.Close();
                        _logger.LogWarning("连接设备超时：{ipAddress}:{port}，尝试下一个IP", ipAddress, notifyRelayPort);
                        continue;
                    }

                    if (!connectTask.IsCompletedSuccessfully)
                    {
                        _logger.LogWarning("连接设备失败：{ipAddress}:{port}，尝试下一个IP", ipAddress, notifyRelayPort);
                        continue;
                    }

                    using var networkStream = tcpClient.GetStream();
                    networkStream.ReadTimeout = (int)timeoutMs;
                    networkStream.WriteTimeout = (int)timeoutMs;

                    await networkStream.WriteAsync(messageBytes, 0, messageBytes.Length);
                    await networkStream.FlushAsync();

                    _logger.LogInformation("成功发送请求：{header}，deviceId={deviceId}", header, device.Id);

                    if (device.IpAddresses.Count > 1)
                    {
                        if (device.IpAddresses[0] != ipAddress)
                        {
                            lock (device)
                            {
                                if (device.IpAddresses[0] != ipAddress)
                                {
                                    device.IpAddresses.Remove(ipAddress);
                                    device.IpAddresses.Insert(0, ipAddress);
                                    _logger.LogInformation("已调整设备IP优先级，下次将优先尝试：{ipAddress}", ipAddress);
                                }
                            }
                        }
                    }

                    return;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "连接或发送到设备失败：{ipAddress}:{port}，尝试下一个IP", ipAddress, notifyRelayPort);
                }
            }

            _logger.LogWarning("所有IP地址都连接失败，跳过发送");
        }
        catch (ObjectDisposedException ex)
        {
            _logger.LogError(ex, "发送请求时 Socket 已释放：deviceId={deviceId}", device.Id);
        }
        catch (SocketException ex)
        {
            _logger.LogError(ex, "发送请求时 Socket 错误：deviceId={deviceId}", device.Id);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "发送请求时出错：deviceId={deviceId}", device.Id);
        }
    }

    /// <summary>
    /// 发送一条加密负载到指定设备，使用默认超时时间。
    /// </summary>
    public Task SendEncryptedAsync(
        PairedDevice device,
        string header,
        string plaintext,
        string localDeviceId,
        string localPublicKey
    )
    {
        return SendEncryptedAsync(device, header, plaintext, localDeviceId, localPublicKey, DEFAULT_TIMEOUT);
    }
}
