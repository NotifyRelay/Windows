using NotifyRelay.Data.AppDatabase.Repository;
using NotifyRelay.Data.Contracts;
using NotifyRelay.Data.Models;
using NotifyRelay.Native;
using NotifyRelay.Services.Socket;

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
    private const int NotifyRelayPort = 23333;

    private readonly ILogger<ProtocolSender> _logger;
    private readonly IDeviceManager _deviceManager;

    public ProtocolSender(
        ILogger<ProtocolSender> logger,
        IDeviceManager deviceManager)
    {
        _logger = logger;
        _deviceManager = deviceManager;
    }

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
        var localPublicKey = NativeCore.GetPublicKey() ?? string.Empty;

        if (localPublicKey is null || localDeviceId is null)
        {
            _logger.LogWarning("本地身份未初始化，跳过发送");
            return;
        }

        await SendEncryptedAsync(device, header, messageJson, localDeviceId, localPublicKey);
    }

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

            string? framedMessage = NativeCore.EncryptMessage(header, localDeviceId, localPublicKey, device.Id, plaintext);
            if (framedMessage == null)
            {
                _logger.LogError("Rust加密失败: EncryptMessage, device={deviceName}", device.Name);
                return;
            }

            _logger.LogDebug("消息字节长度：{length}", framedMessage.Length);

            var ipAddressesCopy = device.IpAddresses.ToList();

            foreach (string ipAddress in ipAddressesCopy)
            {
                _logger.LogInformation("发送到设备：{deviceName} ({ipAddress})", device.Name, ipAddress);

                var success = await OneShotTcpClient.SendOnlyAsync(
                    ipAddress, NotifyRelayPort, framedMessage,
                    DEFAULT_CONNECT_TIMEOUT, timeoutMs);

                if (success)
                {
                    _logger.LogInformation("成功发送请求：{header}，deviceId={deviceId}", header, device.Id);

                    if (device.IpAddresses.Count > 1 && device.IpAddresses[0] != ipAddress)
                    {
                        lock (device)
                        {
                            if (device.IpAddresses[0] != ipAddress)
                            {
                                device.IpAddresses.Remove(ipAddress);
                                device.IpAddresses.Insert(0, ipAddress);
                                _logger.LogInformation("已调整设备IP优先级，下次将优先尝试：{ipAddress}", ipAddress);
                                var repo = Ioc.Default.GetRequiredService<DeviceRepository>();
                                if (repo.HasDevice(device.Id, out var entity))
                                {
                                    entity.IpAddresses = device.IpAddresses;
                                    repo.AddOrUpdateRemoteDevice(entity);
                                }
                            }
                        }
                    }

                    return;
                }

                _logger.LogWarning("连接或发送到设备失败：{ipAddress}:{port}，尝试下一个IP", ipAddress, NotifyRelayPort);
            }

            _logger.LogWarning("所有IP地址都连接失败，跳过发送");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "发送请求时出错：deviceId={deviceId}", device.Id);
        }
    }

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
