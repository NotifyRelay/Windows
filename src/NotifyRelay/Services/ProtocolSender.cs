using NotifyRelay.Data.Contracts;
using NotifyRelay.Data.Models;
using NotifyRelay.Native;

namespace NotifyRelay.Services;

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

            // Rust core 统一使用带 DATA_ 前缀的帧类型标识，接收端（core 归一化表）只识别 DATA_*。
            // 业务报文 type 可能不带前缀（如 ICON_REQUEST/APP_LIST_REQUEST），
            // 发送前统一规范化为 DATA_*，否则对端 core 会归一化为 UNKNOWN 导致消息被平台丢弃。
            // 注意：此规范化曾于 57c0799 随 QUIC V2 回退被误撤销，导致 PC 端应用列表/图标同步失效。
            if (!string.IsNullOrEmpty(header) && !header.StartsWith("DATA_", StringComparison.Ordinal))
            {
                header = "DATA_" + header;
            }

            // 使用 Rust core 发送队列加密发送（IP 由 Rust 内部管理）
            var dedupKey = NativeCore.ComputeDedupKey(device.Id, plaintext);
            NativeCore.EnqueueMessage(device.Id, header, plaintext, dedupKey);

            _logger.LogDebug("消息已入发送队列：header={header}, deviceId={deviceId}", header, device.Id);
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
