using AdvancedSharpAdbClient.Models;
using NotifyRelay.Data.Models;

namespace NotifyRelay.Services.Adb;

/// <summary>
/// 单设备原子操作：按命令序列解锁、锁屏状态判定、按 AndroidId 卸载应用。
/// 不做批量/编排；不参与连接建立。
/// </summary>
public sealed class AdbDeviceOperator(
    IAdbCommandExecutor commandExecutor,
    IAdbDeviceCatalog catalog,
    ILogger<AdbService> logger)
{
    /// <summary>
    /// 解锁设备：仅在锁屏时按 commands 顺序逐条执行，命令间间隔 250ms。
    /// 保持 async void 语义（ScreenMirrorService 以 fire-and-forget 方式调用）。
    /// </summary>
    public async void UnlockDevice(DeviceData deviceData, List<string> commands)
    {
        try
        {
            logger.LogTrace("正在解锁设备");
            if (await IsLocked(deviceData))
            {
                foreach (var command in commands)
                {
                    logger.LogTrace("执行命令：{command}", command);
                    await commandExecutor.ExecuteShellCommandAsync(deviceData, command);
                    await Task.Delay(250);
                }
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "解锁设备时出错：{ex}", ex);
        }
    }

    public async Task<bool> IsLocked(DeviceData deviceData)
    {
        var output = await commandExecutor.ExecuteShellCommandAsync(deviceData, "dumpsys window policy | grep 'showing=' | cut -d '=' -f2");
        return output.Trim() == "true";
    }

    public async Task UninstallApp(string deviceId, string appPackage)
    {
        logger.LogInformation("正在从设备 {deviceId} 卸载应用 {appPackage}", deviceId, appPackage);

        // 在UI线程上查询以避免并发修改
        var adbDevice = await catalog.ReadAsync(d => d.FirstOrDefault(x => x.AndroidId == deviceId));
        if (adbDevice?.DeviceData == null) return;

        var deviceData = adbDevice.DeviceData;
        await commandExecutor.UninstallPackageAsync(deviceData, appPackage);
    }
}
