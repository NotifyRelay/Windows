using System.Net;
using AdvancedSharpAdbClient;
using AdvancedSharpAdbClient.Models;
using CommunityToolkit.WinUI;
using NotifyRelay.Data.Contracts;
using NotifyRelay.Data.Enums;
using NotifyRelay.Data.Items;
using NotifyRelay.Data.Models;
using NotifyRelay.Services.Adb;

namespace NotifyRelay.Services;

public class AdbService : IAdbService
{
    private readonly ILogger<AdbService> logger;
    private readonly IDeviceManager deviceManager;
    private readonly IUserSettingsService userSettingsService;

    private CancellationTokenSource? cts;
    private DeviceMonitor? deviceMonitor;

    // 外部 adb.exe 进程启动器（唯一允许 Process.Start 的地方）
    private readonly AdbProcessLauncher processLauncher = new();

    // ADB 命令执行器（唯一持有 AdbClient 实例的地方）
    private readonly IAdbCommandExecutor commandExecutor;

    // ADB 设备集合目录（唯一持有 AdbDevices 集合实例的地方）
    private readonly IAdbDeviceCatalog catalog = new AdbDeviceCatalog();

    // 设备信息解析器（AndroidId 获取与权限授予）
    private readonly IAdbDeviceInfoResolver infoResolver;

    // 无线 ADB 连接器（建立流程 / 失败冷却 / 防重入）
    private readonly IWirelessAdbConnector wirelessConnector;

    // TCP 重连器（握手触发延迟重连 / 配对设备掉线自动重连）
    private readonly AdbTcpReconnector tcpReconnector;

    public AdbService(
        ILoggerFactory loggerFactory,
        IDeviceManager deviceManager,
        IUserSettingsService userSettingsService)
    {
        this.deviceManager = deviceManager;
        this.userSettingsService = userSettingsService;
        // 日志类别名保持 NotifyRelay.Services.AdbService 不变（由 CreateLogger<AdbService>() 保证）
        logger = loggerFactory.CreateLogger<AdbService>();
        // 执行器复用主体日志类别，保证拆分后 Serilog 类别名与日志文本不变
        commandExecutor = new AdbCommandExecutor(logger);
        infoResolver = new AdbDeviceInfoResolver(commandExecutor, deviceManager, logger);
        // RestartAdbClientAsync 以回调形式注入，解开 A ↔ D 双向依赖
        wirelessConnector = new WirelessAdbConnector(
            commandExecutor, catalog, infoResolver, processLauncher, deviceManager, userSettingsService,
            RestartAdbClientAsync, logger);
        tcpReconnector = new AdbTcpReconnector(commandExecutor, catalog, wirelessConnector, deviceManager, logger);
    }

    public ObservableCollection<AdbDevice> AdbDevices => catalog.Devices;
    public bool IsMonitoring => deviceMonitor != null && !(cts?.IsCancellationRequested ?? true);

    public AdbClient AdbClient => commandExecutor.AdbClient;

    // Initialize the codec option collections
    public ObservableCollection<ScrcpyPreferenceItem> DisplayOrientationOptions => ScrcpyPreferences.DisplayOrientation;

    public ObservableCollection<ScrcpyPreferenceItem> VideoCodecOptions => ScrcpyPreferences.VideoCodec;

    public ObservableCollection<ScrcpyPreferenceItem> AudioCodecOptions => ScrcpyPreferences.AudioCodec;


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
            var existingDevice = await catalog.FindBySerialAsync(e.Device.Serial);
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

                await catalog.AddAsync(adbDevice);
                return;
            }

            // Refresh the full device information
            var connectedDevice = await infoResolver.ResolveAsync(e.Device);

            // Check and grant permissions
            await infoResolver.GrantPermissionsAsync(e.Device);

            await catalog.AddAsync(connectedDevice);
            logger.LogDebug($"设备已连接：{connectedDevice.Model} ({connectedDevice.Serial})");

            // 有线（USB）重新连接 → 清除该设备的无线 ADB 失败冷却，允许重新尝试
            if (connectedDevice.Type == DeviceType.USB && !string.IsNullOrEmpty(connectedDevice.AndroidId))
            {
                wirelessConnector.ClearCooldown(connectedDevice.AndroidId);
            }

            // USB 设备上线时，若已开启 AdbAutoConnect 则自动建立无线 ADB（幂等、无副作用）
            await wirelessConnector.TryEnableForUsbDeviceAsync(connectedDevice);
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
        var existingDevice = await catalog.FindBySerialAsync(e.Device.Serial);
        if (existingDevice != null)
        {
            await catalog.RemoveAsync(existingDevice);
        }
    }

    private async void DeviceChanged(object? sender, DeviceDataChangeEventArgs e)
    {

        logger.LogTrace($"设备状态已更改：{e.Device.Serial} {e.OldState} -> {e.NewState}");

        // 在UI线程上获取existingDevice，避免集合在枚举时被修改
        var existingDevice = await catalog.FindBySerialAsync(e.Device.Serial);

        if (e.NewState == DeviceState.Online)
        {
            var deviceInfo = await infoResolver.ResolveAsync(e.Device);

            if (existingDevice != null)
            {
                // Update existing device using Remove + Add to trigger CollectionChanged
                if (await catalog.ReplaceAsync(existingDevice, deviceInfo))
                {
                    logger.LogDebug($"设备已更新：{deviceInfo.Model} ({deviceInfo.Serial})");
                }
            }
            else
            {
                // Only add if device doesn't exist
                await catalog.AddAsync(deviceInfo);
                logger.LogDebug($"设备已添加：{deviceInfo.Model} ({deviceInfo.Serial})");
            }

            logger.LogDebug($"设备已连接：{deviceInfo.Model} ({deviceInfo.Serial})");

            // 有线（USB）重新连接 → 清除该设备的无线 ADB 失败冷却，允许重新尝试
            if (deviceInfo.Type == DeviceType.USB && !string.IsNullOrEmpty(deviceInfo.AndroidId))
            {
                wirelessConnector.ClearCooldown(deviceInfo.AndroidId);
            }

            // USB 设备上线时，若已开启 AdbAutoConnect 则自动建立无线 ADB（幂等、无副作用）
            await wirelessConnector.TryEnableForUsbDeviceAsync(deviceInfo);
        }
        else
        {
            // Device is going offline/authorizing - just update the state if it exists
            if (existingDevice != null)
            {
                await catalog.UpdateStateAsync(existingDevice, e.NewState);
            }
        }
    }

    private async Task RefreshDevicesAsync()
    {
        var devices = await commandExecutor.GetDevicesAsync();
        if (devices.Any())
        {
            logger.LogWarning("未找到设备");
            await catalog.ClearAsync();
            return;
        }

        foreach (var device in devices)
        {
            AdbDevice adbDevice;
            if (device.State == DeviceState.Online)
            {
                // Get full device info including AndroidId for online devices
                adbDevice = await infoResolver.ResolveAsync(device);
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
            await catalog.AddAsync(adbDevice);
        }

        // 启动时已连接的 USB 设备，若开启 AdbAutoConnect 也自动建立无线 ADB（幂等、无副作用）
        var startupUsbDevices = (await catalog.SnapshotAsync())
            .Where(x => x.Type == DeviceType.USB && x.IsOnline)
            .ToList();
        foreach (var d in startupUsbDevices)
        {
            await wirelessConnector.TryEnableForUsbDeviceAsync(d);
        }
    }

    public Task<bool> ConnectWireless(string? host, int port = 5555)
        => commandExecutor.ConnectAsync(host, port);

    /// <summary>
    /// 幂等地建立无线 ADB 连接（委托给无线连接器）。
    /// </summary>
    public Task<bool> TryEnableWirelessAdbAsync(string hostIp, string? usbSerial = null, string? deviceId = null)
        => wirelessConnector.TryEnableAsync(hostIp, usbSerial, deviceId);

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
        logger.LogInformation("正在从设备 {deviceId} 卸载应用 {appPackage}", appPackage, deviceId);

        // 在UI线程上查询以避免并发修改
        var adbDevice = await catalog.ReadAsync(d => d.FirstOrDefault(x => x.AndroidId == deviceId));
        if (adbDevice?.DeviceData == null) return;

        var deviceData = adbDevice.DeviceData;
        await commandExecutor.UninstallPackageAsync(deviceData, appPackage);
    }

    /// <summary>
    /// Restarts the ADB client to pick up TCP/IP mode changes
    /// </summary>
    internal async Task RestartAdbClientAsync()
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

    public void TryConnectTcp(string host) => tcpReconnector.TryConnectTcp(host);

    public Task<bool> TryAutoReconnectAsync(PairedDevice device) => tcpReconnector.TryAutoReconnectAsync(device);
}
