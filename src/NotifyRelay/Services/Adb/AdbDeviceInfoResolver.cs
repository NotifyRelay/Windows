using AdvancedSharpAdbClient.Models;
using NotifyRelay.Data.Contracts;
using NotifyRelay.Data.Enums;
using NotifyRelay.Data.Models;

namespace NotifyRelay.Services.Adb;

/// <summary>
/// 设备信息解析契约：由 <see cref="DeviceData"/> 解析完整 <see cref="AdbDevice"/>、授予设备权限。
/// </summary>
public interface IAdbDeviceInfoResolver
{
    Task<AdbDevice> ResolveAsync(DeviceData deviceData, CancellationToken ct = default);
    Task GrantPermissionsAsync(DeviceData deviceData, CancellationToken ct = default);
}

/// <summary>
/// 由 <see cref="DeviceData"/> 解析出完整 <see cref="AdbDevice"/>
/// （读 device_info.txt 取 AndroidId，为空时按 Model 模糊匹配已配对设备回退）；
/// 并授予 READ_LOGS 与 READ_CLIPBOARD 权限。
/// 不写入设备集合（只返回对象）；不判断 USB/WIFI 之外的业务规则。
/// </summary>
public sealed class AdbDeviceInfoResolver(
    IAdbCommandExecutor commandExecutor,
    IDeviceManager deviceManager,
    ILogger<AdbService> logger) : IAdbDeviceInfoResolver
{
    public async Task<AdbDevice> ResolveAsync(DeviceData deviceData, CancellationToken ct = default)
    {
        try
        {
            // Get full device information including model
            var devices = await commandExecutor.GetDevicesAsync(ct);
            var fullDeviceData = devices.FirstOrDefault(d => d.Serial == deviceData.Serial);
            if (fullDeviceData == null)
            {
                return new AdbDevice
                {
                    Serial = deviceData.Serial,
                    Model = deviceData.Model ?? "Unknown",
                    State = deviceData.State,
                    Type = deviceData.Serial.Contains(':') || deviceData.Serial.Contains("tcp") ? DeviceType.WIFI : DeviceType.USB,
                    AndroidId = ""
                };
            }
            string androidId = string.Empty;
            try
            {
                logger.LogTrace($"开始获取设备 {deviceData.Serial} 的 UUID");

                // adb shell cat /storage/emulated/0/Android/data/com.xzyht.notifyrelay/files/device_info.txt
                // Get the UUID from the device_info.txt file since we can't directly access the UUID of the App 
                string adbCommand = AdbCommandExecutor.DeviceInfoPath;
                logger.LogTrace($"执行 ADB 命令：{adbCommand}");
                var rawOutput = await commandExecutor.ExecuteShellCommandAsync(deviceData, adbCommand, ct);
                var id = rawOutput.Trim();
                logger.LogTrace($"ADB 命令输出：'{rawOutput}'，处理后：'{id}'");
                if (!string.IsNullOrEmpty(id))
                {
                    // Extract the UUID from the output
                    androidId = id;
                    logger.LogTrace($"成功获取设备 {deviceData.Serial} 的 UUID：{androidId}");
                }
                else
                {
                    logger.LogWarning($"设备 {deviceData.Serial} 的 UUID 为空");
                }
            }
            catch (Exception ex)
            {
                logger.LogError(ex, $"获取 UUID 时出错：{deviceData.Serial}");
            }

            // Look for paired devices with matching model
            if (string.IsNullOrEmpty(androidId) && fullDeviceData.Model != null)
            {
                var deviceModel = fullDeviceData.Model;
                logger.LogTrace($"Android ID 为空，尝试通过设备型号 '{deviceModel}' 匹配已配对设备");

                var pairedDevices = deviceManager.PairedDevices;
                logger.LogTrace($"当前已配对设备数量：{pairedDevices.Count}");

                // (略) 不再逐条输出已配对设备，避免重复日志

                var matchingDevice = pairedDevices.FirstOrDefault(pd =>
                    !string.IsNullOrEmpty(pd.Model) &&
                    (pd.Model.Equals(deviceModel, StringComparison.OrdinalIgnoreCase) ||
                     pd.Model.Contains(deviceModel, StringComparison.OrdinalIgnoreCase) ||
                     deviceModel.Contains(pd.Model, StringComparison.OrdinalIgnoreCase)));

                if (matchingDevice != null)
                {
                    androidId = matchingDevice.Id;
                    logger.LogTrace($"通过型号匹配成功：设备型号 '{deviceModel}' 匹配到已配对设备 ID='{androidId}'，型号='{matchingDevice.Model}'");
                }
                else
                {
                    logger.LogWarning($"未找到与型号 '{deviceModel}' 匹配的配对设备");
                    androidId = string.Empty;
                }
            }

            var device = new AdbDevice
            {
                Serial = fullDeviceData.Serial,
                Model = fullDeviceData.Model ?? "Unknown",
                AndroidId = androidId,
                State = fullDeviceData.State,
                Type = fullDeviceData.Serial.Contains(':') || fullDeviceData.Serial.Contains("tcp") ? DeviceType.WIFI : DeviceType.USB,
                DeviceData = fullDeviceData
            };

            // 添加日志，便于调试
            logger.LogTrace($"生成 ADB 设备对象：序列号='{device.Serial}'，型号='{device.Model}'，Android ID='{device.AndroidId}'，在线状态='{device.IsOnline}'");

            // 检查是否有已配对设备匹配此 ADB 设备
            var allPairedDevices = deviceManager.PairedDevices;
            foreach (var pd in allPairedDevices)
            {
                logger.LogTrace($"检查已配对设备：ID='{pd.Id}'，型号='{pd.Model}'，是否匹配 ADB 设备：{pd.HasAdbConnection}");
            }

            return device;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, $"获取完整设备信息时出错：{deviceData.Serial}");
            // Return basic information if we can't get full details
            var device = new AdbDevice
            {
                Serial = deviceData.Serial,
                Model = "Unknown",
                AndroidId = "Unknown",
                State = deviceData.State,
                Type = deviceData.Serial.Contains(':') || deviceData.Serial.Contains("tcp") ? DeviceType.WIFI : DeviceType.USB,
                DeviceData = deviceData
            };

            return device;
        }
    }

    public async Task GrantPermissionsAsync(DeviceData deviceData, CancellationToken ct = default)
    {
        try
        {
            string packageName = AdbCommandExecutor.PackageName;
            string permission = "android.permission.READ_LOGS";

            logger.LogTrace($"正在检查并授予设备 {deviceData.Serial} 的 {permission} 权限");

            // 直接尝试授予权限，pm grant 是幂等的
            string grantCommand = $"pm grant {packageName} {permission}";

            string result = (await commandExecutor.ExecuteShellCommandAsync(deviceData, grantCommand, ct)).Trim();
            if (string.IsNullOrEmpty(result))
            {
                logger.LogInformation($"成功授予 {permission} 权限给 {packageName}");
            }
            else
            {
                logger.LogTrace($"授予权限结果: {result}");
            }

            // 尝试授予 AppOps READ_CLIPBOARD 权限 (允许后台读取剪贴板)
            // 这可以解决 "Denying clipboard access" 错误
            try
            {
                string appOpsCommand = $"cmd appops set {packageName} READ_CLIPBOARD allow";
                logger.LogTrace($"正在尝试授予 AppOps READ_CLIPBOARD 权限: {appOpsCommand}");
                await commandExecutor.ExecuteShellCommandAsync(deviceData, appOpsCommand, ct);
            }
            catch (Exception ex)
            {
                logger.LogWarning($"尝试授予 AppOps READ_CLIPBOARD 失败 (可能不支持此操作): {ex.Message}");
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, $"尝试授予 READ_LOGS 权限失败：{deviceData.Serial}");
        }
    }
}
