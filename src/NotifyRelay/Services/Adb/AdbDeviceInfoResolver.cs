using AdvancedSharpAdbClient.Models;
using NotifyRelay.Data.Contracts;
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
                // 该数据已从 adb devices 列表中消失，不挂 DeviceData：
                // 下游以 DeviceData == null 作为「快照不可用于发起 adb 调用」的守卫
                return AdbDeviceFactory.CreateBasic(deviceData, attachDeviceData: false);
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
                    // 对端已安装 App 但尚未写入 UUID（如刚安装未启动），属可自愈状态
                    logger.LogDebug("设备 {Serial} 已安装 NotifyRelay 但未取到 UUID", deviceData.Serial);
                }
            }
            catch (Exception ex)
            {
                // device_info.txt 读取失败有两种成因：对端未安装 NotifyRelay（正常情况，直接忽略），
                // 或已安装但尚未生成该文件（可自愈，记为调试日志）。两者都不应作为错误上报。
                if (await IsPackageInstalledAsync(deviceData, ct))
                {
                    logger.LogDebug("设备 {Serial} 已安装 NotifyRelay 但未取到 UUID：{Message}", deviceData.Serial, ex.Message);
                }
                else
                {
                    logger.LogTrace("设备 {Serial} 未安装 NotifyRelay，跳过 UUID 读取", deviceData.Serial);
                }
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

            var device = AdbDeviceFactory.CreateResolved(fullDeviceData, fullDeviceData.Model ?? "Unknown", androidId);

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
            var device = AdbDeviceFactory.CreateResolved(deviceData, "Unknown", "Unknown");

            return device;
        }
    }

    /// <summary>
    /// 判断设备上是否已安装 NotifyRelay。
    /// 未安装时读 UUID / 授权必然失败，属正常情况（对端可能只是没装本应用），不应作为错误上报。
    /// 检查本身失败时保守按「已安装」处理，避免掩盖真实异常。
    /// </summary>
    private async Task<bool> IsPackageInstalledAsync(DeviceData deviceData, CancellationToken ct)
    {
        try
        {
            // 未安装时 adb 返回空输出（不抛异常），已安装时返回 "package:<name>"
            var output = await commandExecutor.ExecuteShellCommandAsync(
                deviceData, $"pm list packages {AdbCommandExecutor.PackageName}", ct);
            return output.Contains(AdbCommandExecutor.PackageName, StringComparison.Ordinal);
        }
        catch (Exception ex)
        {
            logger.LogTrace(ex, "检查设备 {Serial} 是否安装 NotifyRelay 失败", deviceData.Serial);
            return true;
        }
    }

    public async Task GrantPermissionsAsync(DeviceData deviceData, CancellationToken ct = default)
    {
        try
        {
            string packageName = AdbCommandExecutor.PackageName;

            // 对端未安装 NotifyRelay 时授权命令必然失败，属正常情况直接忽略
            if (!await IsPackageInstalledAsync(deviceData, ct))
            {
                logger.LogTrace("设备 {Serial} 未安装 NotifyRelay，跳过权限授予", deviceData.Serial);
                return;
            }

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
