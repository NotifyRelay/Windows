using System.Collections.Concurrent;
using System.Net;
using AdvancedSharpAdbClient;
using AdvancedSharpAdbClient.DeviceCommands;
using AdvancedSharpAdbClient.Models;
using AdvancedSharpAdbClient.Receivers;
using CommunityToolkit.WinUI;
using NotifyRelay.Data.Contracts;
using NotifyRelay.Data.Enums;
using NotifyRelay.Data.Items;
using NotifyRelay.Data.Models;

namespace NotifyRelay.Services;

public class AdbService(
    ILogger<AdbService> logger,
    IDeviceManager deviceManager,
    IUserSettingsService userSettingsService
) : IAdbService
{
    private CancellationTokenSource? cts;
    private DeviceMonitor? deviceMonitor;
    private readonly AdbClient adbClient = new();

    // 防重入/防循环：记录正在处理无线 ADB 建立的 hostIp，避免 adb tcpip 重启 adbd 诱发的重复触发
    private readonly ConcurrentDictionary<string, object?> _pendingWireless = new();

    // 失败冷却（key=配对设备 ID）：无线 ADB 建立失败后，在本次有线连接期间不再重试；
    // 仅当 USB 设备断开后重新连接（有线重新连接）时才清除，允许再次尝试
    private readonly ConcurrentDictionary<string, object?> _wirelessFailCooldown = new();

    public ObservableCollection<AdbDevice> AdbDevices { get; } = [];
    public bool IsMonitoring => deviceMonitor != null && !(cts?.IsCancellationRequested ?? true);

    public AdbClient AdbClient => adbClient;

    // Initialize the codec option collections
    public ObservableCollection<ScrcpyPreferenceItem> DisplayOrientationOptions { get; } =
    [
        new(0, "", "Default"),
        new(1, "0", "0°"),
        new(2, "90", "90°"),
        new(3, "180", "180°"),
        new(4, "270", "270°"),
        new(5, "flip0", "flip-0°"),
        new(6, "flip90", "flip-90°"),
        new(7, "flip180", "flip-180°"),
        new(8, "flip270", "flip-270°")
    ];

    public ObservableCollection<ScrcpyPreferenceItem> VideoCodecOptions { get; } =
    [
        new(0, "", "Default"),
        new(1, "--video-codec=h264 --video-encoder=OMX.qcom.video.encoder.avc", "h264 & c2.qti.avc.encoder (hw)"),
        new(2, "--video-codec=h264 --video-encoder=c2.android.avc.encoder", "h264 & c2.android.avc.encoder (sw)"),
        new(4, "--video-codec=h264 --video-encoder=OMX.google.h264.encoder", "h264 & OMX.google.h264.encoder (sw)"),
        new(5, "--video-codec=h265 --video-encoder=OMX.qcom.video.encoder.hevc", "h265 & OMX.qcom.video.encoder.hevc (hw)"),
        new(6, "--video-codec=h265 --video-encoder=c2.android.hevc.encoder", "h265 & c2.android.hevc.encoder (sw)")
    ];

    public ObservableCollection<ScrcpyPreferenceItem> AudioCodecOptions { get; } =
    [
        new(0, "", "Default"),
        new(1, "--audio-codec=opus --audio-encoder=c2.android.opus.encoder", "opus & c2.android.opus.encoder (sw)"),
        new(2, "--audio-codec=aac --audio-encoder=c2.android.aac.encoder", "aac & c2.android.aac.encoder (sw)"),
        new(3, "--audio-codec=aac --audio-encoder=OMX.google.aac.encoder", "aac & OMX.google.aac.encoder (sw)"),
        new(4, "--audio-codec=raw", "raw")
    ];


    // TODO: To add new options dynamically
    public void AddVideoCodecOption(string command, string display)
    {
        int newId = VideoCodecOptions.Count > 0 ? VideoCodecOptions.Max(x => x.Id) + 1 : 0;
        VideoCodecOptions.Add(new ScrcpyPreferenceItem(newId, command, display));
    }

    public void AddAudioCodecOption(string command, string display)
    {
        int newId = AudioCodecOptions.Count > 0 ? AudioCodecOptions.Max(x => x.Id) + 1 : 0;
        AudioCodecOptions.Add(new ScrcpyPreferenceItem(newId, command, display));
    }



    public async Task StartAsync()
    {
        try
        {
            if (IsMonitoring) return;

            cts = new CancellationTokenSource();
            string adbPath = $"{userSettingsService.GeneralSettingsService.AdbPath}";

            // Start the ADB server if it's not running
            StartServerResult startServerResult = await AdbServer.Instance.StartServerAsync(adbPath, false, cts.Token);
            logger.LogTrace($"ADB 服务启动结果：{startServerResult}");

            // Create and configure the device monitor
            deviceMonitor = new DeviceMonitor(new AdbSocket(new IPEndPoint(IPAddress.Loopback, AdbClient.AdbServerPort)));

            deviceMonitor.DeviceConnected += DeviceConnected;
            deviceMonitor.DeviceDisconnected += DeviceDisconnected;
            deviceMonitor.DeviceChanged += DeviceChanged;

            await Task.Delay(50);

            await deviceMonitor.StartAsync();

            // Get initial list of devices
            await RefreshDevicesAsync();

            logger.LogTrace("ADB 设备监控已成功启动");
        }
        catch (Exception ex)
        {
            await CleanupAsync();
            logger.LogError("启动 ADB 设备监控失败：{ex}", ex);
        }
    }

    public async Task StopAsync()
    {
        if (!IsMonitoring)
        {
            logger.LogWarning("ADB 监控未在运行");
            return;
        }

        await CleanupAsync();
        logger.LogInformation("ADB 设备监控已停止");
    }

    private async Task CleanupAsync()
    {
        if (deviceMonitor != null)
        {
            deviceMonitor.DeviceConnected -= DeviceConnected;
            deviceMonitor.DeviceDisconnected -= DeviceDisconnected;
            deviceMonitor.DeviceChanged -= DeviceChanged;

            await deviceMonitor.DisposeAsync();
            deviceMonitor = null;
        }

        if (cts != null)
        {
            cts.Cancel();
            cts.Dispose();
            cts = null;
        }
    }

    private async void DeviceConnected(object? sender, DeviceDataEventArgs e)
    {
        try
        {
            // Check if device already exists in collection (在UI线程上获取以避免并发修改)
            AdbDevice? existingDevice = null;
            await App.MainWindow.DispatcherQueue.EnqueueAsync(() =>
            {
                existingDevice = AdbDevices.FirstOrDefault(d => d.Serial == e.Device.Serial);
            });
            if (existingDevice != null) return;

            // get the rudimentary data if it isn't online yet
            if (e.Device.State != DeviceState.Online)
            {
                logger.LogTrace($"设备 {e.Device.Serial} 已连接，但尚未在线，当前状态：{e.Device.State}");

                var adbDevice = new AdbDevice
                {
                    Serial = e.Device.Serial,
                    Model = e.Device.Model ?? "Unknown",
                    State = e.Device.State,
                    Type = e.Device.Serial.Contains(':') || e.Device.Serial.Contains("tcp") ? DeviceType.WIFI : DeviceType.USB,
                    DeviceData = e.Device,
                    AndroidId = "" // Will be populated when device comes online
                };

                await App.MainWindow.DispatcherQueue.EnqueueAsync(() =>
                {
                    AdbDevices.Add(adbDevice);
                });
                return;
            }

            // Refresh the full device information
            var connectedDevice = await GetFullDeviceInfoAsync(e.Device);

            // Check and grant permissions
            await CheckAndGrantLogPermissionAsync(e.Device);

            await App.MainWindow.DispatcherQueue.EnqueueAsync(() =>
            {
                AdbDevices.Add(connectedDevice);
            });
            logger.LogDebug($"设备已连接：{connectedDevice.Model} ({connectedDevice.Serial})");

            // 有线（USB）重新连接 → 清除该设备的无线 ADB 失败冷却，允许重新尝试
            if (connectedDevice.Type == DeviceType.USB && !string.IsNullOrEmpty(connectedDevice.AndroidId))
            {
                _wirelessFailCooldown.TryRemove(connectedDevice.AndroidId, out _);
            }

            // USB 设备上线时，若已开启 AdbAutoConnect 则自动建立无线 ADB（幂等、无副作用）
            await TryEnableWirelessForUsbDeviceAsync(connectedDevice);
        }
        catch (Exception ex)
        {
            logger.LogError($"处理设备连接时出错 {e.Device.Serial}：{ex.Message}", ex);
        }
    }

    private async void DeviceDisconnected(object? sender, DeviceDataEventArgs e)
    {
        logger.LogTrace($"设备已断开：{e.Device.Serial}");
        // 在UI线程上获取existingDevice，避免集合在枚举时被修改
        AdbDevice? existingDevice = null;
        await App.MainWindow.DispatcherQueue.EnqueueAsync(() =>
        {
            existingDevice = AdbDevices.FirstOrDefault(d => d.Serial == e.Device.Serial);
        });
        if (existingDevice != null)
        {
            await App.MainWindow.DispatcherQueue.EnqueueAsync(() =>
            {
                var index = AdbDevices.IndexOf(existingDevice);
                if (index != -1)
                {
                    AdbDevices.RemoveAt(index);
                }
            });
        }
    }

    private async void DeviceChanged(object? sender, DeviceDataChangeEventArgs e)
    {

        logger.LogTrace($"设备状态已更改：{e.Device.Serial} {e.OldState} -> {e.NewState}");

        // 在UI线程上获取existingDevice，避免集合在枚举时被修改
        AdbDevice? existingDevice = null;
        await App.MainWindow.DispatcherQueue.EnqueueAsync(() =>
        {
            existingDevice = AdbDevices.FirstOrDefault(d => d.Serial == e.Device.Serial);
        });

        if (e.NewState == DeviceState.Online)
        {
            var deviceInfo = await GetFullDeviceInfoAsync(e.Device);

            await App.MainWindow.DispatcherQueue.EnqueueAsync(() =>
            {
                if (existingDevice != null)
                {
                    // Update existing device using Remove + Add to trigger CollectionChanged
                    var index = AdbDevices.IndexOf(existingDevice);
                    if (index != -1)
                    {
                        AdbDevices.RemoveAt(index);
                        AdbDevices.Insert(index, deviceInfo);
                        logger.LogDebug($"设备已更新：{deviceInfo.Model} ({deviceInfo.Serial})");
                    }
                }
                else
                {
                    // Only add if device doesn't exist
                    AdbDevices.Add(deviceInfo);
                    logger.LogDebug($"设备已添加：{deviceInfo.Model} ({deviceInfo.Serial})");
                }
            });

            logger.LogDebug($"设备已连接：{deviceInfo.Model} ({deviceInfo.Serial})");

            // 有线（USB）重新连接 → 清除该设备的无线 ADB 失败冷却，允许重新尝试
            if (deviceInfo.Type == DeviceType.USB && !string.IsNullOrEmpty(deviceInfo.AndroidId))
            {
                _wirelessFailCooldown.TryRemove(deviceInfo.AndroidId, out _);
            }

            // USB 设备上线时，若已开启 AdbAutoConnect 则自动建立无线 ADB（幂等、无副作用）
            await TryEnableWirelessForUsbDeviceAsync(deviceInfo);
        }
        else
        {
            // Device is going offline/authorizing - just update the state if it exists
            if (existingDevice != null)
            {
                await App.MainWindow.DispatcherQueue.EnqueueAsync(() =>
                {
                    var index = AdbDevices.IndexOf(existingDevice);
                    if (index != -1)
                    {
                        // Update using Remove + Insert to trigger CollectionChanged
                        existingDevice.State = e.NewState;
                        AdbDevices.RemoveAt(index);
                        AdbDevices.Insert(index, existingDevice);
                    }
                });
            }
        }
    }

    private async Task RefreshDevicesAsync()
    {
        var devices = await adbClient.GetDevicesAsync();
        if (devices.Any())
        {
            logger.LogWarning("未找到设备");
            await App.MainWindow.DispatcherQueue.EnqueueAsync(() =>
            {
                AdbDevices.Clear();
            });
            return;
        }

        await App.MainWindow.DispatcherQueue.EnqueueAsync(async () =>
        {
            var adbDevices = new List<AdbDevice>();
            foreach (var device in devices)
            {
                AdbDevice adbDevice;
                if (device.State == DeviceState.Online)
                {
                    // Get full device info including AndroidId for online devices
                    adbDevice = await GetFullDeviceInfoAsync(device);
                }
                else
                {
                    // Create basic device info for non-online devices
                    adbDevice = new AdbDevice
                    {
                        Serial = device.Serial,
                        Model = device.Model ?? "Unknown",
                        State = device.State,
                        Type = device.Serial.Contains(':') || device.Serial.Contains("tcp") ? DeviceType.WIFI : DeviceType.USB,
                        DeviceData = device,
                        AndroidId = ""
                    };
                }
                AdbDevices.Add(adbDevice);
            }

            // 启动时已连接的 USB 设备，若开启 AdbAutoConnect 也自动建立无线 ADB（幂等、无副作用）
            var startupUsbDevices = new List<AdbDevice>();
            foreach (var d in AdbDevices.Where(x => x.Type == DeviceType.USB && x.IsOnline))
            {
                startupUsbDevices.Add(d);
            }
            foreach (var d in startupUsbDevices)
            {
                await TryEnableWirelessForUsbDeviceAsync(d);
            }
        });
    }

    private async Task<AdbDevice> GetFullDeviceInfoAsync(DeviceData deviceData)
    {
        try
        {
            // Get full device information including model
            var devices = await adbClient.GetDevicesAsync();
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
                var uuidReceiver = new ConsoleOutputReceiver();

                // adb shell cat /storage/emulated/0/Android/data/com.xzyht.notifyrelay/files/device_info.txt
                // Get the UUID from the device_info.txt file since we can't directly access the UUID of the App 
                string adbCommand = "cat /storage/emulated/0/Android/data/com.xzyht.notifyrelay/files/device_info.txt";
                logger.LogTrace($"执行 ADB 命令：{adbCommand}");
                await adbClient.ExecuteShellCommandAsync(deviceData, adbCommand, uuidReceiver);
                var rawOutput = uuidReceiver.ToString();
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

    private async Task CheckAndGrantLogPermissionAsync(DeviceData deviceData)
    {
        try
        {
            string packageName = "com.xzyht.notifyrelay";
            string permission = "android.permission.READ_LOGS";

            logger.LogTrace($"正在检查并授予设备 {deviceData.Serial} 的 {permission} 权限");

            // 直接尝试授予权限，pm grant 是幂等的
            string grantCommand = $"pm grant {packageName} {permission}";
            var receiver = new ConsoleOutputReceiver();

            await adbClient.ExecuteShellCommandAsync(deviceData, grantCommand, receiver);

            string result = receiver.ToString().Trim();
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
                await adbClient.ExecuteShellCommandAsync(deviceData, appOpsCommand, receiver);

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

    public async Task<bool> ConnectWireless(string? host, int port = 5555)
    {
        if (string.IsNullOrEmpty(host)) return false;

        try
        {
            var result = await adbClient.ConnectAsync(host, port);
            if (result.Contains("failed") || result.Contains("refused"))
            {
                return false;
            }
            return true;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "连接默认无线设备时出错");
            return false;
        }
    }

    public async Task<bool> Pair(AdbDevice device, string pairingCode, string host, int port = 5555)
    {
        if (string.IsNullOrEmpty(host)) return false;
        try
        {
            var result = await adbClient.PairAsync(host, port, pairingCode);
            if (result.Contains("failed") || result.Contains("refused"))
            {
                return false;
            }
            return true;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "连接无线设备 {device} 时出错：{ex}", device.Serial, ex);
            return false;
        }
    }

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
                    await adbClient.ExecuteShellCommandAsync(deviceData, command);
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
        ConsoleOutputReceiver consoleReceiver = new();
        await adbClient.ExecuteShellCommandAsync(deviceData, "dumpsys window policy | grep 'showing=' | cut -d '=' -f2", consoleReceiver);
        return consoleReceiver.ToString().Trim() == "true";
    }

    public async Task UninstallApp(string deviceId, string appPackage)
    {
        logger.LogInformation("正在从设备 {deviceId} 卸载应用 {appPackage}", appPackage, deviceId);

        // 在UI线程上查询以避免并发修改
        AdbDevice? adbDevice = null;
        await App.MainWindow.DispatcherQueue.EnqueueAsync(() =>
        {
            adbDevice = AdbDevices.FirstOrDefault(d => d.AndroidId == deviceId);
        });
        if (adbDevice?.DeviceData == null) return;

        var deviceData = adbDevice.DeviceData;
        await adbClient.UninstallPackageAsync(deviceData, appPackage);
    }

    /// <summary>
    /// Enables TCP/IP mode by restarting ADB with tcpip 5555 command
    /// </summary>
    private async Task<bool> EnableTcpipMode(string? targetSerial = null)
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
                var listInfo = new ProcessStartInfo
                {
                    FileName = adbPath,
                    Arguments = "devices -l",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                using var listProc = Process.Start(listInfo);
                if (listProc != null)
                {
                    var listOut = await listProc.StandardOutput.ReadToEndAsync();
                    var listErr = await listProc.StandardError.ReadToEndAsync();
                    await listProc.WaitForExitAsync();
                    logger.LogTrace("adb devices 输出:\n{Out}", listOut);
                    if (!string.IsNullOrEmpty(listErr)) logger.LogWarning("adb devices 错误输出: {Err}", listErr);
                }
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "执行 adb devices 时出错");
            }

            // Run "adb tcpip 5555" (如果提供了 targetSerial，则使用 -s 指定设备，避免 'more than one device' 错误)
            var tcpipArgs = string.IsNullOrEmpty(targetSerial) ? "tcpip 5555" : $"-s {targetSerial} tcpip 5555";
            logger.LogTrace("将执行 adb 命令: {Args}", tcpipArgs);

            var processInfo = new ProcessStartInfo
            {
                FileName = adbPath,
                Arguments = tcpipArgs,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = Process.Start(processInfo);
            if (process == null)
            {
                logger.LogError("启动 ADB 进程失败");
                return false;
            }

            await process.WaitForExitAsync();
            var output = await process.StandardOutput.ReadToEndAsync();
            var error = await process.StandardError.ReadToEndAsync();

            if (!string.IsNullOrEmpty(output)) logger.LogInformation("adb tcpip 输出: {Out}", output);
            if (!string.IsNullOrEmpty(error)) logger.LogWarning("adb tcpip 错误输出: {Err}", error);

            if (!string.IsNullOrEmpty(error) && error.Contains("more than one device", StringComparison.OrdinalIgnoreCase))
            {
                logger.LogWarning("检测到多个设备：请在启用 tcpip 时指定目标设备序列号，或确保仅连接目标设备。错误信息：{Err}", error);
            }

            // Restart our ADB client to pick up the changes
            await RestartAdbClient();

            return process.ExitCode == 0;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "启用 TCP/IP 模式失败");
            return false;
        }
    }

    /// <summary>
    /// Restarts the ADB client to pick up TCP/IP mode changes
    /// </summary>
    private async Task RestartAdbClient()
    {
        try
        {
            logger.LogTrace("正在重启 ADB 客户端");
            var wasMonitoring = IsMonitoring;
            if (wasMonitoring)
            {
                await CleanupAsync();
            }
            await Task.Delay(200);

            if (wasMonitoring)
            {
                await StartAsync();
            }
            logger.LogTrace("ADB 客户端重启成功");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "重启 ADB 客户端失败");
        }
    }

    /// <summary>
    /// 幂等地建立无线 ADB 连接。
    /// 1) 若目标 hostIp:5555 已在线，直接返回（幂等，不重启 adbd）；
    /// 2) 先尝试 adb connect（无副作用）；
    /// 3) 仅当直连失败且提供了 usbSerial 时，才对该 USB 设备执行一次 adb tcpip 5555 再重试，
    ///    以避免在 AS 安装等过程中重复重启 adbd 造成打断。
    /// </summary>
    public async Task<bool> TryEnableWirelessAdbAsync(string hostIp, string? usbSerial = null, string? deviceId = null)
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
            bool alreadyConnected = false;
            await App.MainWindow.DispatcherQueue.EnqueueAsync(() =>
            {
                alreadyConnected = AdbDevices.Any(d => d.Serial == $"{hostIp}:5555" && d.IsOnline);
            });
            if (alreadyConnected)
            {
                logger.LogTrace("无线 ADB {Host}:5555 已连接，跳过（幂等）", hostIp);
                return true;
            }

            // 先尝试直连（无副作用）
            if (await ConnectWireless(hostIp))
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
                var tcpipEnabled = await EnableTcpipMode(usbSerial);
                if (!tcpipEnabled)
                {
                    logger.LogError("启用 TCP/IP 模式失败：{Serial}", usbSerial);
                    RecordWirelessFailure(deviceId);
                    return false;
                }

                await Task.Delay(200);

                if (await ConnectWireless(hostIp))
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
            var devices = await adbClient.GetDevicesAsync();
            var device = devices.FirstOrDefault(d => d.Serial == $"{hostIp}:5555");
            if (device == null)
            {
                logger.LogWarning("无线设备 {Host}:5555 未出现在 adb devices 中", hostIp);
                return false;
            }

            var receiver = new ConsoleOutputReceiver();
            await adbClient.ExecuteShellCommandAsync(
                device,
                "cat /storage/emulated/0/Android/data/com.xzyht.notifyrelay/files/device_info.txt",
                receiver);
            var text = receiver.ToString().Trim();
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
            bool exists = false;
            await App.MainWindow.DispatcherQueue.EnqueueAsync(() =>
            {
                exists = AdbDevices.Any(d => d.Serial == serial);
            });
            if (exists) return;

            // adb connect 成功后设备通常不会立即出现在设备列表中，轮询几次以覆盖时序竞态
            DeviceData? deviceData = null;
            for (int i = 0; i < 5 && deviceData == null; i++)
            {
                if (i > 0) await Task.Delay(400);
                var devices = await adbClient.GetDevicesAsync();
                deviceData = devices.FirstOrDefault(d => d.Serial == serial);
            }

            if (deviceData == null)
            {
                logger.LogWarning("adb connect 成功但 GetDevicesAsync 仍未找到设备 {Serial}", serial);
                return;
            }

            var adbDevice = await GetFullDeviceInfoAsync(deviceData);
            await App.MainWindow.DispatcherQueue.EnqueueAsync(() =>
            {
                if (!AdbDevices.Any(d => d.Serial == serial))
                {
                    AdbDevices.Add(adbDevice);
                }
            });
            logger.LogDebug("已将无线 ADB 设备同步进设备列表：{Serial}", serial);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "同步无线 ADB 设备 {Host}:5555 进列表失败", hostIp);
        }
    }

    /// <summary>
    /// 当检测到有线(USB) ADB 设备上线且对应已配对设备开启了 AdbAutoConnect 时，
    /// 自动打开该设备的无线 ADB 并连接（幂等、无副作用）。
    /// </summary>
    private async Task TryEnableWirelessForUsbDeviceAsync(AdbDevice usbDevice)
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

            await TryEnableWirelessAdbAsync(ip.Trim(), usbDevice.Serial, paired.Id);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "处理 USB 设备 {Serial} 无线 ADB 自动连接时出错", usbDevice.Serial);
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
                var receiver = new ConsoleOutputReceiver();
                await adbClient.ExecuteShellCommandAsync(deviceData, "ip route get 0.0.0.0", receiver);
                var output = receiver.ToString();
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

    /// <summary>
    /// 根据握手主机 IP 解析应执行 adb tcpip 的 USB 设备序列号（多设备安全）。
    /// 优先按 IP 匹配已配对设备并取其 USB 设备；若仅有一个在线 USB 设备则兼容使用之；
    /// 多设备且无法匹配时返回 null 以避免误伤其它设备。
    /// </summary>
    private async Task<string?> FindUsbSerialForHostAsync(string host)
    {
        string? serial = null;
        await App.MainWindow.DispatcherQueue.EnqueueAsync(() =>
        {
            var paired = deviceManager.PairedDevices.FirstOrDefault(pd =>
                (pd.IpAddresses != null && pd.IpAddresses.Any(ip => string.Equals(ip?.Trim(), host, StringComparison.OrdinalIgnoreCase))) ||
                string.Equals(pd.RemoteIpAddress?.Trim(), host, StringComparison.OrdinalIgnoreCase));
            if (paired != null)
            {
                var usb = AdbDevices.FirstOrDefault(d => d.Type == DeviceType.USB && !string.IsNullOrEmpty(d.AndroidId) && d.AndroidId == paired.Id);
                serial = usb?.Serial;
            }

            if (string.IsNullOrEmpty(serial))
            {
                var usbDevices = AdbDevices.Where(d => d.Type == DeviceType.USB && d.IsOnline).ToList();
                if (usbDevices.Count == 1)
                {
                    serial = usbDevices[0].Serial;
                }
                else if (usbDevices.Count > 1)
                {
                    logger.LogWarning("存在多个 USB 设备且无法按 IP {Host} 匹配，跳过错配的 adb tcpip", host);
                }
            }
        });
        return serial;
    }

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
            if (_wirelessFailCooldown.ContainsKey(deviceId))
            {
                logger.LogTrace("握手触发无线 ADB：设备 {DeviceId} 失败冷却中，跳过", deviceId);
                return;
            }

            // 相对无感的触发：设备被标记在线后，延迟 5s，期间若未离线才建立无线 ADB。
            // 这样可避免在瞬时握手/抖动时立即动作，从而不轻易打断正在进行的操作（如 AS 安装）。
            logger.LogDebug("握手触发无线 ADB：设备 {Host} 已上线，延迟 5s 观察是否保持在线", host);
            await Task.Delay(5000);

            // 延迟期间可能已进入失败冷却，二次检查
            if (_wirelessFailCooldown.ContainsKey(deviceId))
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

            await TryEnableWirelessAdbAsync(host, usbSerial, deviceId);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "尝试连接 {Host} 时发生错误", host);
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

    public async Task<bool> TryAutoReconnectAsync(PairedDevice device)
    {
        try
        {
            logger.LogInformation("尝试自动重连设备 {DeviceName} ({DeviceId})", device.Name, device.Id);

            // 检查当前设备是否已经有对应的ADB连接 (在UI线程上执行以避免并发修改)
            bool hasConnection = false;
            await App.MainWindow.DispatcherQueue.EnqueueAsync(() =>
            {
                hasConnection = AdbDevices.Any(adbDevice =>
                    adbDevice.IsOnline &&
                    (
                        (!string.IsNullOrEmpty(adbDevice.AndroidId) && adbDevice.AndroidId == device.Id) ||
                        (string.IsNullOrEmpty(adbDevice.AndroidId) &&
                            !string.IsNullOrEmpty(adbDevice.Model) &&
                            !string.IsNullOrEmpty(device.Model) &&
                            (device.Model.Equals(adbDevice.Model, StringComparison.OrdinalIgnoreCase) ||
                             device.Model.Contains(adbDevice.Model, StringComparison.OrdinalIgnoreCase) ||
                             adbDevice.Model.Contains(device.Model, StringComparison.OrdinalIgnoreCase)))
                    ));
            });

            if (hasConnection)
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
                var connected = await ConnectWireless(ip, 5555);
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
                        AdbDevice? newDevice = null;
                        await App.MainWindow.DispatcherQueue.EnqueueAsync(() =>
                        {
                            newDevice = AdbDevices.FirstOrDefault(d => d.Serial == deviceSerial && d.IsOnline);
                        });
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
}
