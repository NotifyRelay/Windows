using NotifyRelay.Data.Contracts;
using NotifyRelay.Data.Models;

namespace NotifyRelay.Platforms.Windows.Services;

public class WindowftpService(
    ILogger logger,
    NetworkDriveMapper networkDriveMapper
    ) : IftpService
{
    public async Task InitializeAsync(PairedDevice device, string payload)
    {
        // 这个方法现在由ProtocolRouter直接调用NetworkDriveMapper处理，这里只是保持接口兼容
        logger.LogInformation("FTP服务初始化已由ProtocolRouter直接处理，此方法仅保持接口兼容");
        await Task.CompletedTask;
    }

    public void Remove(string deviceId)
    {
        try
        {
            logger.LogInformation("正在移除设备 {DeviceId} 的FTP网络磁盘映射", deviceId);
            // 使用网络磁盘映射服务断开连接
            networkDriveMapper.UnmapftpDrive(deviceId);
            logger.LogInformation("设备 {DeviceId} 的FTP网络磁盘映射已成功移除", deviceId);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "移除设备 {DeviceId} 的FTP网络磁盘映射失败", deviceId);
        }
    }
}