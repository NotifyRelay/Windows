using System;
using System.Linq;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using NotifyRelay.Data.Contracts;
using NotifyRelay.Data.Models;
using NotifyRelay.Helpers;

namespace NotifyRelay.Services;

/// <summary>
/// 统一加密发送器
/// 
/// 封装加密、认证检查、TCP发送与报文头拼装：
/// 最终报文格式：`<HEADER>:<localUuid>:<localPublicKey>:<encryptedPayload>\n`
/// </summary>
public static class ProtocolSender
{
    private const string TAG = "ProtocolSender";
    private const int DEFAULT_TIMEOUT = 80000;
    private const int DEFAULT_CONNECT_TIMEOUT = 5000;

    /// <summary>
    /// 发送消息，自动从JSON中提取type作为协议头
    /// </summary>
    /// <param name="logger">日志记录器</param>
    /// <param name="deviceManager">设备管理器</param>
    /// <param name="deviceId">目标设备ID</param>
    /// <param name="messageJson">消息JSON字符串</param>
    public static async Task SendMessageAsync(
        ILogger logger,
        IDeviceManager deviceManager,
        string deviceId,
        string messageJson)
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
            logger.LogWarning(ex, "解析消息JSON以提取type失败，使用默认头DATA_JSON");
        }

        await SendMessageAsync(logger, deviceManager, deviceId, messageJson, header);
    }
    
    /// <summary>
    /// 发送消息，指定协议头
    /// </summary>
    /// <param name="logger">日志记录器</param>
    /// <param name="deviceManager">设备管理器</param>
    /// <param name="deviceId">目标设备ID</param>
    /// <param name="messageJson">消息JSON字符串</param>
    /// <param name="header">协议头</param>
    public static async Task SendMessageAsync(
        ILogger logger,
        IDeviceManager deviceManager,
        string deviceId,
        string messageJson,
        string header)
    {
        var device = deviceManager.PairedDevices.FirstOrDefault(d => d.Id == deviceId);
        if (device is null)
        {
            logger.LogWarning("跳过发送：未找到设备 {deviceId}", deviceId);
            return;
        }

        var localDevice = await deviceManager.GetLocalDeviceAsync();
        var localDeviceId = localDevice.DeviceId;
        var localPublicKey = Encoding.UTF8.GetString(localDevice.PublicKey ?? Array.Empty<byte>());

        if (localPublicKey is null || localDeviceId is null)
        {
            logger.LogWarning("本地身份未初始化，跳过发送");
            return;
        }

        await SendEncryptedAsync(logger, device, header, messageJson, localDeviceId, localPublicKey);
    }
    
    /// <summary>
    /// 发送一条加密负载到指定设备。
    /// </summary>
    /// <param name="logger">日志记录器</param>
    /// <param name="device">目标设备</param>
    /// <param name="header">消息头，例如：DATA_JSON / DATA_ICON_REQUEST / DATA_ICON_RESPONSE 等</param>
    /// <param name="plaintext">明文内容</param>
    /// <param name="localDeviceId">本地设备ID</param>
    /// <param name="localPublicKey">本地公钥</param>
    /// <param name="timeoutMs">超时时间</param>
    public static async Task SendEncryptedAsync(
        ILogger logger,
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
            // 检查设备是否已认证
            if (device.SharedSecret == null)
            {
                logger.LogWarning("设备未认证或未接受：{deviceName}", device.Name);
                return;
            }

            // 确保设备有IP地址
            if (device.IpAddresses == null || device.IpAddresses.Count == 0)
            {
                logger.LogWarning("设备 {deviceName} 没有可用的IP地址", device.Name);
                return;
            }

            const int notifyRelayPort = 23333;

            // 加密消息
            string encryptedPayload = NotifyCryptoHelper.Encrypt(plaintext, device.SharedSecret);
            
            // 构建最终消息
            string framedMessage = $"{header}:{localDeviceId}:{localPublicKey}:{encryptedPayload}\n";
            byte[] messageBytes = Encoding.UTF8.GetBytes(framedMessage);
            
            logger.LogDebug("消息字节长度：{length}", messageBytes.Length);

            // 创建IP地址列表的副本，避免在遍历过程中修改原集合导致InvalidOperationException
            var ipAddressesCopy = device.IpAddresses.ToList();
            
            // 遍历设备的所有IP地址，尝试连接
            foreach (string ipAddress in ipAddressesCopy)
            {
                logger.LogInformation("发送到设备：{deviceName} ({ipAddress})", device.Name, ipAddress);

                // 创建TCP客户端并发送消息
                using var tcpClient = new TcpClient();
                tcpClient.ReceiveTimeout = (int)timeoutMs;
                tcpClient.SendTimeout = (int)timeoutMs;
                
                try
                {
                    // 连接设备
                    var connectTask = tcpClient.ConnectAsync(ipAddress, notifyRelayPort);
                    var delayTask = Task.Delay(DEFAULT_CONNECT_TIMEOUT);
                    var connectResult = await Task.WhenAny(connectTask, delayTask);
                    
                    // 确保所有任务都完成，避免未处理异常
                    if (connectResult == delayTask)
                    {
                        // 超时，取消连接任务
                        tcpClient.Close();
                        logger.LogWarning("连接设备超时：{ipAddress}:{port}，尝试下一个IP", ipAddress, notifyRelayPort);
                        continue;
                    }
                    
                    // 检查连接任务是否成功
                    if (!connectTask.IsCompletedSuccessfully)
                    {
                        logger.LogWarning("连接设备失败：{ipAddress}:{port}，尝试下一个IP", ipAddress, notifyRelayPort);
                        continue;
                    }
                    
                    using var networkStream = tcpClient.GetStream();
                    networkStream.ReadTimeout = (int)timeoutMs;
                    networkStream.WriteTimeout = (int)timeoutMs;
                    
                    // 发送消息
                    await networkStream.WriteAsync(messageBytes, 0, messageBytes.Length);
                    // 确保数据完全发送
                    await networkStream.FlushAsync();
                    
                    logger.LogInformation("成功发送请求：{header}，deviceId={deviceId}", header, device.Id);
                    
                    // 将成功的IP地址移到列表首位，下次优先尝试
                    if (device.IpAddresses.Count > 1)
                    {
                        // 检查当前IP是否已经是首位，避免不必要的操作和日志
                        if (device.IpAddresses[0] != ipAddress)
                        {
                            // 使用lock确保线程安全，避免并发修改异常
                            lock (device)
                            {
                                // 再次检查，防止并发修改
                                if (device.IpAddresses[0] != ipAddress)
                                {
                                    device.IpAddresses.Remove(ipAddress);
                                    device.IpAddresses.Insert(0, ipAddress);
                                    logger.LogInformation("已调整设备IP优先级，下次将优先尝试：{ipAddress}", ipAddress);
                                }
                            }
                        }
                    }
                    
                    return; // 发送成功，退出循环
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "连接或发送到设备失败：{ipAddress}:{port}，尝试下一个IP", ipAddress, notifyRelayPort);
                    // 继续尝试下一个IP地址
                }
            }

            // 所有IP地址都尝试失败
            logger.LogWarning("所有IP地址都连接失败，跳过发送");
        }
        catch (ObjectDisposedException ex)
        {
            logger.LogError(ex, "发送请求时 Socket 已释放：deviceId={deviceId}", device.Id);
        }
        catch (SocketException ex)
        {
            logger.LogError(ex, "发送请求时 Socket 错误：deviceId={deviceId}", device.Id);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "发送请求时出错：deviceId={deviceId}", device.Id);
        }
    }

    /// <summary>
    /// 发送一条加密负载到指定设备，使用默认超时时间。
    /// </summary>
    public static Task SendEncryptedAsync(
        ILogger logger,
        PairedDevice device,
        string header,
        string plaintext,
        string localDeviceId,
        string localPublicKey
    )
    {
        return SendEncryptedAsync(logger, device, header, plaintext, localDeviceId, localPublicKey, DEFAULT_TIMEOUT);
    }
}
