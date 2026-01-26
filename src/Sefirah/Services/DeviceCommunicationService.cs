using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using NotifyRelay.Data.Contracts;
using NotifyRelay.Data.Models;
using NotifyRelay.Utils;

namespace NotifyRelay.Services;

public interface IDeviceCommunicationService
{
    void SendAppListRequest(string deviceId);
    void SendIconRequest(string deviceId, List<string> packageNames);
    void SendIconRequest(string deviceId, string packageName);
    void SendMediaControlRequest(string deviceId, string controlType);
    void SendMediaPlayNotification(string deviceId, NotificationMessage mediaInfo);
    void SendMediaPlayNotification(string deviceId, NotificationMessage mediaInfo, string mediaType);
    void SendMessage(string deviceId, string message);
    void BroadcastMessage(string message);
}

public class DeviceCommunicationService(
    ILogger<DeviceCommunicationService> logger,
    IDeviceManager deviceManager) : IDeviceCommunicationService
{
    private ObservableCollection<PairedDevice> PairedDevices => deviceManager.PairedDevices;

    /// <summary>
    /// 发送应用列表请求
    /// </summary>
    /// <param name="deviceId">设备 ID</param>
    public void SendAppListRequest(string deviceId)
    {
        // 构建应用列表请求对象
        var requestObj = new
        {
            type = "APP_LIST_REQUEST",
            scope = "user",
            time = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
        };
        
        // 序列化为 JSON
        string requestJson = JsonSerializer.Serialize(requestObj);
        
        // 调用通用发送方法
        SendRequest(deviceId, "DATA_APP_LIST_REQUEST", requestJson, "应用列表请求");
    }
    
    /// <summary>
    /// 发送图标请求
    /// </summary>
    /// <param name="deviceId">设备 ID</param>
    /// <param name="packageNames">应用包名列表</param>
    public void SendIconRequest(string deviceId, List<string> packageNames)
    {
        logger.LogInformation("开始发送图标请求：deviceId={deviceId}, packageCount={packageCount}", deviceId, packageNames.Count);

        // 构建图标请求对象（支持单个或多个包名）
        object requestObj;
        if (packageNames.Count == 1)
        {
            requestObj = new
            {
                type = "ICON_REQUEST",
                packageName = packageNames.First(),
                time = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
            };
        }
        else
        {
            requestObj = new
            {
                type = "ICON_REQUEST",
                packageNames = packageNames,
                time = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
            };
        }
        
        // 序列化为 JSON
        string requestJson = JsonSerializer.Serialize(requestObj);
        
        // 调用通用发送方法
        SendRequest(deviceId, "DATA_ICON_REQUEST", requestJson, $"图标请求，packageCount={packageNames.Count}");
    }
    
    /// <summary>
    /// 发送图标请求（单个包名）
    /// </summary>
    /// <param name="deviceId">设备 ID</param>
    /// <param name="packageName">应用包名</param>
    public void SendIconRequest(string deviceId, string packageName)
    {
        SendIconRequest(deviceId, new List<string> { packageName });
    }

    /// <summary>
    /// 发送媒体控制请求
    /// </summary>
    /// <param name="deviceId">设备 ID</param>
    /// <param name="controlType">控制类型（如 play, pause, next 等）</param>
    public void SendMediaControlRequest(string deviceId, string controlType)
    {
        // 构建媒体控制请求对象，与 Android 端保持一致（移除time字段）
        var requestObj = new
        {
            type = "MEDIA_CONTROL",
            action = controlType
        };
        
        // 序列化为 JSON
        string requestJson = JsonSerializer.Serialize(requestObj);
        
        // 调用通用发送方法，使用 DATA_MEDIA_CONTROL 协议头，与 Android 端保持一致
        SendRequest(deviceId, "DATA_MEDIA_CONTROL", requestJson, $"媒体控制请求，controlType={controlType}");
    }
    
    /// <summary>
    /// 发送媒体播放通知
    /// </summary>
    /// <param name="deviceId">设备 ID</param>
    /// <param name="mediaInfo">媒体播放信息</param>
    public void SendMediaPlayNotification(string deviceId, NotificationMessage mediaInfo)
    {
        // 调用重载方法，默认发送全量包
        SendMediaPlayNotification(deviceId, mediaInfo, "FULL");
    }
    
    /// <summary>
    /// 发送媒体播放通知
    /// </summary>
    /// <param name="deviceId">设备 ID</param>
    /// <param name="mediaInfo">媒体播放信息</param>
    /// <param name="mediaType">媒体类型，FULL 表示全量包，DELTA 表示差异包</param>
    public void SendMediaPlayNotification(string deviceId, NotificationMessage mediaInfo, string mediaType)
    {
        // 构建媒体播放通知对象，与 Android 端保持一致
        var requestObj = new
        {
            type = "MEDIA_PLAY",
            packageName = mediaInfo.AppPackage,
            appName = mediaInfo.AppName,
            title = mediaInfo.Title,
            text = mediaInfo.Text,
            coverUrl = mediaInfo.CoverUrl,
            time = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            isLocked = false, // Android端需要的字段，默认设为false
            mediaType = mediaType // Android端需要的字段，支持FULL和DELTA
        };
        
        // 序列化为 JSON
        string requestJson = JsonSerializer.Serialize(requestObj);
        
        // 调用通用发送方法，使用 DATA_MEDIAPLAY 协议头，与 Android 端保持一致
        SendRequest(deviceId, "DATA_MEDIAPLAY", requestJson, "媒体播放通知");
    }

    public void SendMessage(string deviceId, string message)
    {
        logger.LogDebug("原始消息内容：{message}", message);
        
        // 根据消息内容选择消息类型
        string messageType = "DATA_JSON";
        
        // 直接检查消息中的 type 字段值或内容特征
        if (message.Contains("APP_LIST_REQUEST", StringComparison.OrdinalIgnoreCase))
        {
            messageType = "DATA_APP_LIST_REQUEST";
        }
        else if (message.Contains("ICON_REQUEST", StringComparison.OrdinalIgnoreCase))
        {
            messageType = "DATA_ICON_REQUEST";
        }
        else if (message.Contains("AUDIO_RESPONSE", StringComparison.OrdinalIgnoreCase))
        {
            messageType = "DATA_AUDIO_RESPONSE";
        }
        else if (message.Contains("MEDIA_CONTROL", StringComparison.OrdinalIgnoreCase))
        {
            messageType = "DATA_MEDIA_CONTROL";
        }
        else if (message.Contains("MEDIA_PLAY", StringComparison.OrdinalIgnoreCase))
        {
            messageType = "DATA_MEDIAPLAY";
        }
        else if (message.Contains("DATA_FTP", StringComparison.OrdinalIgnoreCase) || 
                 message.Contains("ftpServerInfo", StringComparison.OrdinalIgnoreCase))
        {
            messageType = "DATA_FTP";
        }
        else if (message.Contains("clipboardType", StringComparison.OrdinalIgnoreCase) && 
                 message.Contains("content", StringComparison.OrdinalIgnoreCase))
        {
            // 识别剪贴板消息，根据content和clipboardType字段
            messageType = "DATA_CLIPBOARD";
        }
        else
        {
        }
        
        // 根据消息类型设置描述
        string description = messageType switch
        {
            "DATA_JSON" => "通用消息",
            "DATA_APP_LIST_REQUEST" => "应用列表请求",
            "DATA_ICON_REQUEST" => "图标请求",
            "DATA_AUDIO_RESPONSE" => "音频响应",
            "DATA_MEDIA_CONTROL" => "媒体控制",
            "DATA_MEDIAPLAY" => "媒体播放",
            "DATA_FTP" => "ftp操作",
            "DATA_CLIPBOARD" => "剪贴板消息",
            _ => "通用消息"
        };

        // 调用通用发送方法
        SendRequest(deviceId, messageType, message, description);
    }

    public void BroadcastMessage(string message)
    {
        try
        {
            // 获取所有已连接的设备ID
            // 这里我们认为只要在PairedDevices中且ConnectionStatus为true的都是连接的
            // NetworkService中原本是根据SessionMap来判断，但这里我们无法访问SessionMap
            // 依赖DeviceManager的状态是合理的
            var targets = PairedDevices.Where(d => d.ConnectionStatus).Select(d => d.Id).ToList();
            foreach (var deviceId in targets)
            {
                SendMessage(deviceId, message);
            }
        }
        catch (Exception ex)
        {
            logger.LogError("向所有设备发送消息时出错：{ex}", ex);
        }
    }

    /// <summary>
    /// 通用发送请求方法
    /// </summary>
    /// <param name="deviceId">设备 ID</param>
    /// <param name="messageType">消息类型（如 DATA_APP_LIST_REQUEST, DATA_ICON_REQUEST, DATA_MEDIA_CONTROL 等）</param>
    /// <param name="requestJson">请求内容的 JSON 字符串</param>
    /// <param name="description">请求描述，用于日志</param>
    private void SendRequest(string deviceId, string messageType, string requestJson, string description)
    {
        logger.LogInformation("开始发送请求：{description}，deviceId={deviceId}", description, deviceId);
        
        _ = Task.Run(async () =>
        {
            try
            {
                var device = PairedDevices.FirstOrDefault(d => d.Id == deviceId);
                if (device is null)
                {
                    logger.LogWarning("跳过发送：未找到设备 {deviceId}", deviceId);
                    return;
                }
                
                // 获取本地设备信息
                var localDevice = await deviceManager.GetLocalDeviceAsync();
                var localDeviceId = localDevice.DeviceId;
                var localPublicKey = Encoding.UTF8.GetString(localDevice.PublicKey ?? Array.Empty<byte>());

                if (localPublicKey is null || localDeviceId is null)
                {
                    logger.LogWarning("本地身份未初始化，跳过发送");
                    return;
                }
                
                // 使用统一的协议发送器发送消息
                await ProtocolSender.SendEncryptedAsync(
                    logger, 
                    device, 
                    messageType, 
                    requestJson, 
                    localDeviceId, 
                    localPublicKey
                );
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "发送请求时出错：deviceId={deviceId}", deviceId);
            }
        });
    }
}
