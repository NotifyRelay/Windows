using NotifyRelay.Data.Contracts;
using NotifyRelay.Data.Models;
using NotifyRelay.Native;

namespace NotifyRelay.Services.Protocol;

/// <summary>
/// 统一加密发送器 - 通过 Rust core 发送队列发送
/// </summary>
public class ProtocolSender : IProtocolSender
{
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

        if (localDeviceId is null)
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
        int timeoutMs = 80000
    )
    {
        try
        {
            if (NativeCore.ExportDeviceKey(device.Id) == null)
            {
                _logger.LogWarning("设备未认证或未接受：{deviceName}", device.Name);
                return;
            }

            // header 规范化、去重键推导与发送去重（仅通知类）均在 Rust core 的入队入口内部完成（IP 同样由 Rust 管理）。
            var result = NativeCore.EnqueueMessage(device.Id, header, plaintext);
            if (result == 1)
            {
                _logger.LogDebug("消息已入发送队列：header={header}, deviceId={deviceId}", header, device.Id);
            }
            else if (result == 0)
            {
                _logger.LogDebug("命中去重，未入队：header={header}, deviceId={deviceId}", header, device.Id);
            }
            else
            {
                _logger.LogWarning("入队失败（参数无效或队列未初始化）：header={header}, deviceId={deviceId}", header, device.Id);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "发送请求时出错：deviceId={deviceId}", device.Id);
        }
        await Task.CompletedTask;
    }

    public Task SendEncryptedAsync(
        PairedDevice device,
        string header,
        string plaintext,
        string localDeviceId,
        string localPublicKey
    )
    {
        return SendEncryptedAsync(device, header, plaintext, localDeviceId, localPublicKey, 80000);
    }
}
