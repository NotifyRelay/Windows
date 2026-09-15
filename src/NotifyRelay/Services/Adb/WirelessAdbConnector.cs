using System.Collections.Concurrent;
using System.Net;
using AdvancedSharpAdbClient.Models;
using CommunityToolkit.WinUI;
using NotifyRelay.Data.Contracts;
using NotifyRelay.Data.Enums;
using NotifyRelay.Data.Models;

namespace NotifyRelay.Services.Adb;

/// <summary>
/// 无线 ADB 建立契约：按 host/usbSerial/deviceId 幂等建立、为 USB 设备自动建立、清除与查询失败冷却。
/// </summary>
public interface IWirelessAdbConnector
{
    Task<bool> TryEnableAsync(string hostIp, string? usbSerial = null, string? deviceId = null);
    Task TryEnableForUsbDeviceAsync(AdbDevice usbDevice);

    /// <summary>USB 重新连接时清除该设备的失败冷却。</summary>
    void ClearCooldown(string deviceId);

    bool IsCoolingDown(string deviceId);
}

/// <summary>
/// 无线 ADB 全流程编排：幂等短路 → 直连 → 文件校验 → 必要时 adb tcpip → 重连 → 同步设备进列表。
/// 持有失败冷却与防重入两个并发字典。
/// 不持有 AdbDevices 集合（经 <see cref="IAdbDeviceCatalog"/> 操作）；
/// 不自行重启 ADB 客户端（经构造注入的 <c>restartAdbClient</c> 回调）。
/// </summary>
public sealed class WirelessAdbConnector(
    IAdbCommandExecutor commandExecutor,
    IAdbDeviceCatalog catalog,
    IAdbDeviceInfoResolver infoResolver,
    AdbProcessLauncher processLauncher,
    IDeviceManager deviceManager,
    IUserSettingsService userSettingsService,
    Func<Task> restartAdbClient,
    ILogger<AdbService> logger) : IWirelessAdbConnector
{
    // 防重入/防循环：记录正在处理无线 ADB 建立的 hostIp，避免 adb tcpip 重启 adbd 诱发的重复触发
    private readonly ConcurrentDictionary<string, object?> _pendingWireless = new();

    // 失败冷却（key=配对设备 ID）：无线 ADB 建立失败后，在本次有线连接期间不再重试；
    // 仅当 USB 设备断开后重新连接（有线重新连接）时才清除，允许再次尝试
    private readonly ConcurrentDictionary<string, object?> _wirelessFailCooldown = new();

    public bool IsCoolingDown(string deviceId) => _wirelessFailCooldown.ContainsKey(deviceId);

    public void ClearCooldown(string deviceId) => _wirelessFailCooldown.TryRemove(deviceId, out _);

    /// <summary>
    /// 幂等地建立无线 ADB 连接。
    /// 1) 若目标 hostIp:5555 已在线，直接返回（幂等，不重启 adbd）；
    /// 2) 先尝试 adb connect（无副作用）；
    /// 3) 仅当直连失败且提供了 usbSerial 时，才对该 USB 设备执行一次 adb tcpip 5555 再重试，
    ///    以避免在 AS 安装等过程中重复重启 adbd 造成打断。
    /// </summary>
    public async Task<bool> TryEnableAsync(string hostIp, string? usbSerial = null, string? deviceId = null)
    {
        try
        {
            // 失败冷却：本次有线连接期间失败过则不重试
            if (!string.IsNullOrEmpty(deviceId) && _wirelessFailCooldown.ContainsKey(deviceId))
            {
                logger.LogTrace("无线 ADB {Host}:5555 失败冷却中，跳过", hostIp);
                return false;
            }

            // 幂等：若已存在该 hostIp:5555 在线无线设备，直接返回
            if (await catalog.IsWirelessOnlineAsync(hostIp))
            {
                logger.LogTrace("无线 ADB {Host}:5555 已连接，跳过（幂等）", hostIp);
                return true;
            }

            // 先尝试直连（无副作用）
            if (await commandExecutor.ConnectAsync(hostIp))
            {
                // 成功判定：须通过无线连接读取到目标文件文本，而非仅 adb connect 返回成功
                if (await VerifyWirelessFileAsync(hostIp))
                {
                    logger.LogDebug("直连无线 ADB 成功：{Host}:5555", hostIp);
                    if (!string.IsNullOrEmpty(deviceId)) _wirelessFailCooldown.TryRemove(deviceId, out _);
                    await AddWirelessDeviceToListIfMissingAsync(hostIp);
                    return true;
                }
                logger.LogWarning("无线 ADB {Host}:5555 连接成功但目标文件文本验证失败，按失败处理", hostIp);
            }

            // 直连失败且需要提供 USB 序列号时才启用 tcpip（会重启 adbd）
            if (string.IsNullOrEmpty(usbSerial))
            {
                logger.LogDebug("无法直连 {Host}:5555 且无可用 USB 设备序列号，跳过启用 tcpip", hostIp);
                RecordWirelessFailure(deviceId);
                return false;
            }

            // 防重入/防循环：同一 IP 正在处理则跳过
            if (!_pendingWireless.TryAdd(hostIp, null))
            {
                logger.LogTrace("无线 ADB {Host} 正在处理中，跳过重复触发", hostIp);
                return false;
            }

            try
            {
                logger.LogDebug("将对 USB 设备 {Serial} 启用 TCP/IP 模式以建立无线 ADB {Host}:5555", usbSerial, hostIp);
                var tcpipEnabled = await EnableTcpipModeAsync(usbSerial);
                if (!tcpipEnabled)
                {
                    logger.LogError("启用 TCP/IP 模式失败：{Serial}", usbSerial);
                    RecordWirelessFailure(deviceId);
                    return false;
                }

                await Task.Delay(200);

                if (await commandExecutor.ConnectAsync(hostIp))
                {
                    if (await VerifyWirelessFileAsync(hostIp))
                    {
                        logger.LogDebug("启用 TCP/IP 模式后成功连接无线 ADB {Host}:5555", hostIp);
                        if (!string.IsNullOrEmpty(deviceId)) _wirelessFailCooldown.TryRemove(deviceId, out _);
                        await AddWirelessDeviceToListIfMissingAsync(hostIp);
                        return true;
                    }
                    logger.LogWarning("启用 TCP/IP 模式后无线 ADB {Host}:5555 目标文件文本验证失败，按失败处理", hostIp);
                }

                logger.LogError("启用 TCP/IP 模式后仍无法连接无线 ADB {Host}:5555", hostIp);
                RecordWirelessFailure(deviceId);
                return false;
            }
            finally
            {
                _pendingWireless.TryRemove(hostIp, out _);
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "建立无线 ADB {Host} 时出错", hostIp);
            RecordWirelessFailure(deviceId);
            return false;
        }
    }

    /// <summary>
    /// 当检测到有线(USB) ADB 设备上线且对应已配对设备开启了 AdbAutoConnect 时，
    /// 自动打开该设备的无线 ADB 并连接（幂等、无副作用）。
    /// </summary>
    public async Task TryEnableForUsbDeviceAsync(AdbDevice usbDevice)
    {
        try
        {
            if (usbDevice.Type != DeviceType.USB || !usbDevice.IsOnline) return;

            // 有线成功判据：必须已获取到目标文件文本（AndroidId 非空）且与已配对设备严格匹配
            var paired = await FindPairedDeviceAsync(usbDevice);
            if (paired == null)
            {
                logger.LogTrace("USB 设备 {Serial} 未匹配到已配对设备，跳过无线 ADB 自动连接", usbDevice.Serial);
                return;
            }
            if (!paired.DeviceSettings.AdbAutoConnect)
            {
                logger.LogTrace("USB 设备 {Serial} 的 AdbAutoConnect 未开启，跳过", usbDevice.Serial);
                return;
            }
            if (_wirelessFailCooldown.ContainsKey(paired.Id))
            {
                logger.LogTrace("USB 设备 {Serial} 的无线 ADB 处于失败冷却中，本次有线连接期间不再重试", usbDevice.Serial);
                return;
            }

            var ip = await GetWirelessIpAsync(paired, usbDevice.DeviceData);
            if (string.IsNullOrWhiteSpace(ip))
            {
                logger.LogWarning("无法获取 USB 设备 {Serial} 的 WiFi IP，跳过无线 ADB 自动连接", usbDevice.Serial);
                return;
            }

            await TryEnableAsync(ip.Trim(), usbDevice.Serial, paired.Id);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "处理 USB 设备 {Serial} 无线 ADB 自动连接时出错", usbDevice.Serial);
        }
    }

    /// <summary>
    /// Enables TCP/IP mode by restarting ADB with tcpip 5555 command
    /// </summary>
    private async Task<bool> EnableTcpipModeAsync(string? targetSerial = null)
    {
        try
        {
            string adbPath = userSettingsService.GeneralSettingsService.AdbPath;
            if (string.IsNullOrEmpty(adbPath))
            {
                logger.LogError("ADB 路径未配置");
                return false;
            }

            logger.LogTrace("正在使用 ADB（{AdbPath}）启用 TCP/IP 模式，目标序列：{Target}", adbPath, targetSerial ?? "<any>");

            // 先列出当前 adb devices，帮助诊断多设备情况
            try
            {
                var listResult = await processLauncher.RunAsync(adbPath, "devices -l");
                if (listResult != null)
                {
                    logger.LogTrace("adb devices 输出:\n{Out}", listResult.StandardOutput);
                    if (!string.IsNullOrEmpty(listResult.StandardError)) logger.LogWarning("adb devices 错误输出: {Err}", listResult.StandardError);
                }
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "执行 adb devices 时出错");
            }

            // Run "adb tcpip 5555" (如果提供了 targetSerial，则使用 -s 指定设备，避免 'more than one device' 错误)
            var tcpipArgs = string.IsNullOrEmpty(targetSerial) ? "tcpip 5555" : $"-s {targetSerial} tcpip 5555";
            logger.LogTrace("将执行 adb 命令: {Args}", tcpipArgs);

            var processResult = await processLauncher.RunAsync(adbPath, tcpipArgs);
            if (processResult == null)
            {
                logger.LogError("启动 ADB 进程失败");
                return false;
            }

            var output = processResult.StandardOutput;
            var error = processResult.StandardError;

            if (!string.IsNullOrEmpty(output)) logger.LogInformation("adb tcpip 输出: {Out}", output);
            if (!string.IsNullOrEmpty(error)) logger.LogWarning("adb tcpip 错误输出: {Err}", error);

            if (!string.IsNullOrEmpty(error) && error.Contains("more than one device", StringComparison.OrdinalIgnoreCase))
            {
                logger.LogWarning("检测到多个设备：请在启用 tcpip 时指定目标设备序列号，或确保仅连接目标设备。错误信息：{Err}", error);
            }

            // Restart our ADB client to pick up the changes
            await restartAdbClient();

            return processResult.ExitCode == 0;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "启用 TCP/IP 模式失败");
            return false;
        }
    }

    /// <summary>
    /// 记录无线 ADB 失败冷却（key=配对设备 ID），本次有线连接期间不再重试。
    /// </summary>
    private void RecordWirelessFailure(string? deviceId)
    {
        if (!string.IsNullOrEmpty(deviceId))
        {
            _wirelessFailCooldown.TryAdd(deviceId, null);
        }
    }

    /// <summary>
    /// 通过无线连接读取目标文件（device_info.txt）文本，非空才算无线 ADB 真正建立成功。
    /// </summary>
    private async Task<bool> VerifyWirelessFileAsync(string hostIp)
    {
        try
        {
            var devices = await commandExecutor.GetDevicesAsync();
            var device = devices.FirstOrDefault(d => d.Serial == $"{hostIp}:5555");
            if (device == null)
            {
                logger.LogWarning("无线设备 {Host}:5555 未出现在 adb devices 中", hostIp);
                return false;
            }

            var text = (await commandExecutor.ReadDeviceInfoFileAsync(device)).Trim();
            if (string.IsNullOrEmpty(text))
            {
                logger.LogWarning("无线设备 {Host}:5555 目标文件文本为空", hostIp);
                return false;
            }
            logger.LogDebug("无线设备 {Host}:5555 目标文件文本验证通过", hostIp);
            return true;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "验证无线设备 {Host}:5555 目标文件文本时出错", hostIp);
            return false;
        }
    }

    /// <summary>
    /// 在 adb connect 成功后，主动将无线设备同步进 AdbDevices，
    /// 避免依赖易漏的 DeviceMonitor 事件（某些设备/时序下 monitor 不会补发上线事件）。
    /// </summary>
    private async Task AddWirelessDeviceToListIfMissingAsync(string hostIp)
    {
        try
        {
            var serial = $"{hostIp}:5555";
            if (await catalog.ExistsAsync(serial)) return;

            // adb connect 成功后设备通常不会立即出现在设备列表中，轮询几次以覆盖时序竞态
            DeviceData? deviceData = null;
            for (int i = 0; i < 5 && deviceData == null; i++)
            {
                if (i > 0) await Task.Delay(400);
                var devices = await commandExecutor.GetDevicesAsync();
                deviceData = devices.FirstOrDefault(d => d.Serial == serial);
            }

            if (deviceData == null)
            {
                logger.LogWarning("adb connect 成功但 GetDevicesAsync 仍未找到设备 {Serial}", serial);
                return;
            }

            var adbDevice = await infoResolver.ResolveAsync(deviceData);
            await catalog.AddIfMissingAsync(adbDevice);
            logger.LogDebug("已将无线 ADB 设备同步进设备列表：{Serial}", serial);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "同步无线 ADB 设备 {Host}:5555 进列表失败", hostIp);
        }
    }

    private async Task<PairedDevice?> FindPairedDeviceAsync(AdbDevice usbDevice)
    {
        PairedDevice? result = null;
        await App.MainWindow.DispatcherQueue.EnqueueAsync(() =>
        {
            // 有线成功判据：目标文件文本（AndroidId）已获取且与已配对设备严格匹配
            if (!string.IsNullOrEmpty(usbDevice.AndroidId))
            {
                result = deviceManager.PairedDevices.FirstOrDefault(pd => pd.Id == usbDevice.AndroidId);
            }
        });
        return result;
    }

    private async Task<string?> GetWirelessIpAsync(PairedDevice paired, DeviceData? deviceData)
    {
        if (paired.IpAddresses != null)
        {
            foreach (var ip in paired.IpAddresses)
            {
                if (!string.IsNullOrWhiteSpace(ip)) return ip.Trim();
            }
        }
        if (!string.IsNullOrWhiteSpace(paired.RemoteIpAddress)) return paired.RemoteIpAddress.Trim();

        if (deviceData != null)
        {
            try
            {
                var output = await commandExecutor.ExecuteShellCommandAsync(deviceData, "ip route get 0.0.0.0");
                var idx = output.IndexOf("src ", StringComparison.OrdinalIgnoreCase);
                if (idx >= 0)
                {
                    var src = output.Substring(idx + 4).Trim().Split(' ')[0];
                    if (IPAddress.TryParse(src, out _)) return src;
                }
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "通过 adb shell 获取设备 WiFi IP 失败");
            }
        }
        return null;
    }
}
