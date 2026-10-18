using AdvancedSharpAdbClient.Models;
using CommunityToolkit.WinUI;
using NotifyRelay.Data.Contracts;
using NotifyRelay.Data.Enums;
using NotifyRelay.Data.Models;

namespace NotifyRelay.Services.Adb;

/// <summary>
/// 握手触发的延迟重连与配对设备掉线后自动重连。
/// 不执行 adb tcpip（委托给 <see cref="IWirelessAdbConnector"/>）；不直接读写设备集合。
/// </summary>
public sealed class AdbTcpReconnector(
    IAdbCommandExecutor commandExecutor,
    IAdbDeviceCatalog catalog,
    IWirelessAdbConnector wirelessConnector,
    IDeviceManager deviceManager,
    ILogger<AdbService> logger)
{
    /// <summary>
    /// 握手触发的延迟重连（5 秒观察窗）。
    /// 保持 async void 语义（NetworkService 以 fire-and-forget 方式调用）：
    /// 内部异常必须全部捕获，否则会导致进程崩溃。
    /// </summary>
    public async void TryConnectTcp(string host)
    {
        try
        {
            // 反查配对设备 ID（冷却按设备 ID 维度，USB 重连时可精确清除）
            string? deviceId = null;
            await App.MainWindow.DispatcherQueue.EnqueueAsync(() =>
            {
                deviceId = deviceManager.PairedDevices.FirstOrDefault(pd =>
                    (pd.IpAddresses != null && pd.IpAddresses.Any(ip => string.Equals(ip?.Trim(), host, StringComparison.OrdinalIgnoreCase))) ||
                    string.Equals(pd.RemoteIpAddress?.Trim(), host, StringComparison.OrdinalIgnoreCase))?.Id;
            });
            if (string.IsNullOrEmpty(deviceId))
            {
                logger.LogTrace("握手触发无线 ADB：{Host} 未匹配到已配对设备，跳过", host);
                return;
            }
            if (wirelessConnector.IsCoolingDown(deviceId))
            {
                logger.LogTrace("握手触发无线 ADB：设备 {DeviceId} 失败冷却中，跳过", deviceId);
                return;
            }

            // 无线 ADB 已连接则直接跳过，避免握手反复触发 5s 观察流程
            if (await catalog.IsWirelessOnlineAsync(host))
            {
                logger.LogTrace("握手触发无线 ADB：设备 {Host} 无线 ADB 已在线，跳过", host);
                return;
            }

            // 相对无感的触发：设备被标记在线后，延迟 5s，期间若未离线才建立无线 ADB。
            // 这样可避免在瞬时握手/抖动时立即动作，从而不轻易打断正在进行的操作（如 AS 安装）。
            logger.LogDebug("握手触发无线 ADB：设备 {Host} 已上线，延迟 5s 观察是否保持在线", host);
            await Task.Delay(5000);

            // 延迟期间可能已进入失败冷却，二次检查
            if (wirelessConnector.IsCoolingDown(deviceId))
            {
                logger.LogTrace("握手触发无线 ADB：设备 {DeviceId} 延迟期间进入失败冷却，取消", deviceId);
                return;
            }
            if (!await IsPairedDeviceOnlineAsync(host))
            {
                logger.LogDebug("延迟 5s 后设备 {Host} 已离线，取消无线 ADB 自动连接", host);
                return;
            }

            var usbSerial = await FindUsbSerialForHostAsync(host);
            if (string.IsNullOrEmpty(usbSerial))
            {
                logger.LogDebug("未找到与 {Host} 匹配的 USB 设备，仅尝试直连无线 ADB", host);
            }

            await wirelessConnector.TryEnableAsync(host, usbSerial, deviceId);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "尝试连接 {Host} 时发生错误", host);
        }
    }

    public async Task<bool> TryAutoReconnectAsync(PairedDevice device)
    {
        try
        {
            logger.LogInformation("尝试自动重连设备 {DeviceName} ({DeviceId})", device.Name, device.Id);

            // 检查当前设备是否已经有对应的ADB连接 (在UI线程上执行以避免并发修改)
            if (await catalog.HasConnectionForAsync(device))
            {
                logger.LogDebug("设备 {DeviceName} 已有对应的ADB连接，跳过自动重连", device.Name);
                return true;
            }

            // 尝试从多个来源获取IP地址
            List<string> possibleIps = [];

            // 1. 从Session获取IP地址
            if (device.Session?.Socket?.RemoteEndPoint != null)
            {
                var sessionIp = device.Session.Socket.RemoteEndPoint.ToString()?.Split(':')[0];
                if (!string.IsNullOrEmpty(sessionIp))
                {
                    possibleIps.Add(sessionIp);
                    logger.LogDebug("从Session获取到IP地址: {Ip}", sessionIp);
                }
            }

            // 2. 从RemoteIpAddress获取
            if (!string.IsNullOrEmpty(device.RemoteIpAddress))
            {
                possibleIps.Add(device.RemoteIpAddress);
                logger.LogDebug("从RemoteIpAddress获取到IP地址: {Ip}", device.RemoteIpAddress);
            }

            // 3. 从IpAddresses列表获取
            if (device.IpAddresses != null && device.IpAddresses.Count > 0)
            {
                possibleIps.AddRange(device.IpAddresses);
                logger.LogDebug("从IpAddresses列表获取到 {Count} 个IP地址", device.IpAddresses.Count);
            }

            // 去重
            possibleIps = possibleIps.Distinct().ToList();

            if (possibleIps.Count == 0)
            {
                logger.LogWarning("设备 {DeviceName} 没有可用的IP地址，无法自动重连", device.Name);
                return false;
            }

            logger.LogInformation("找到 {Count} 个可能的IP地址，尝试连接5555端口", possibleIps.Count);

            // 尝试连接每个IP地址的5555端口
            foreach (var ip in possibleIps)
            {
                logger.LogDebug("尝试连接 {Ip}:5555", ip);
                var connected = await commandExecutor.ConnectAsync(ip, 5555);
                if (connected)
                {
                    logger.LogInformation("成功自动重连到设备 {DeviceName}，IP: {Ip}:5555", device.Name, ip);

                    // 等待设备出现在ADB设备列表中
                    var maxWaitTime = TimeSpan.FromSeconds(5);
                    var startTime = DateTime.Now;
                    var deviceSerial = $"{ip}:5555";

                    while (DateTime.Now - startTime < maxWaitTime)
                    {
                        // 在UI线程上查询以避免并发修改
                        var newDevice = await catalog.ReadAsync(d => d.FirstOrDefault(x => x.Serial == deviceSerial && x.IsOnline));
                        if (newDevice != null)
                        {
                            logger.LogDebug("设备 {Serial} 已出现在ADB设备列表中", deviceSerial);
                            return true;
                        }
                        await Task.Delay(100);
                    }

                    logger.LogWarning("设备 {Serial} 连接成功但未在预期时间内出现在设备列表中", deviceSerial);
                    return true;
                }
            }

            logger.LogWarning("尝试了所有IP地址，自动重连失败");
            return false;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "自动重连设备 {DeviceName} 时发生错误", device.Name);
            return false;
        }
    }

    /// <summary>
    /// 判断与指定主机 IP 对应的已配对设备是否仍处于在线状态（握手未断开）。
    /// </summary>
    private async Task<bool> IsPairedDeviceOnlineAsync(string host)
    {
        bool online = false;
        await App.MainWindow.DispatcherQueue.EnqueueAsync(() =>
        {
            var paired = deviceManager.PairedDevices.FirstOrDefault(pd =>
                (pd.IpAddresses != null && pd.IpAddresses.Any(ip => string.Equals(ip?.Trim(), host, StringComparison.OrdinalIgnoreCase))) ||
                string.Equals(pd.RemoteIpAddress?.Trim(), host, StringComparison.OrdinalIgnoreCase));
            online = paired != null && paired.ConnectionStatus;
        });
        return online;
    }

    /// <summary>
    /// 根据握手主机 IP 解析应执行 adb tcpip 的 USB 设备序列号（多设备安全）。
    /// 优先按 IP 匹配已配对设备并取其 USB 设备；若仅有一个在线 USB 设备则兼容使用之；
    /// 多设备且无法匹配时返回 null 以避免误伤其它设备。
    /// </summary>
    private async Task<string?> FindUsbSerialForHostAsync(string host)
    {
        // 配对设备集合的读取需在 UI 线程上执行
        PairedDevice? paired = null;
        await App.MainWindow.DispatcherQueue.EnqueueAsync(() =>
        {
            paired = deviceManager.PairedDevices.FirstOrDefault(pd =>
                (pd.IpAddresses != null && pd.IpAddresses.Any(ip => string.Equals(ip?.Trim(), host, StringComparison.OrdinalIgnoreCase))) ||
                string.Equals(pd.RemoteIpAddress?.Trim(), host, StringComparison.OrdinalIgnoreCase));
        });

        string? serial = null;
        if (paired != null)
        {
            var pairedId = paired.Id;
            serial = await catalog.ReadAsync(d =>
                d.FirstOrDefault(x => x.Type == DeviceType.USB && !string.IsNullOrEmpty(x.AndroidId) && x.AndroidId == pairedId)?.Serial);
        }

        if (string.IsNullOrEmpty(serial))
        {
            var usbDevices = await catalog.ReadAsync(d => d.Where(x => x.Type == DeviceType.USB && x.IsOnline).ToList()) ?? [];
            if (usbDevices.Count == 1)
            {
                serial = usbDevices[0].Serial;
            }
            else if (usbDevices.Count > 1)
            {
                logger.LogWarning("存在多个 USB 设备且无法按 IP {Host} 匹配，跳过错配的 adb tcpip", host);
            }
        }
        return serial;
    }
}
