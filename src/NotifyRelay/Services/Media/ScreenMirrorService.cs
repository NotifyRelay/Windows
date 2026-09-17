using NotifyRelay.Data.Contracts;
using NotifyRelay.Data.Models;

namespace NotifyRelay.Services.Media;

public class ScreenMirrorService : IScreenMirrorService, IDisposable
{
    // 主构造函数参数以字段形式保留（拆分后需在构造函数体内构造协作者，故改为显式构造函数）
    private readonly ILogger<ScreenMirrorService> logger;
    private readonly IUserSettingsService userSettingsService;
    private readonly IAdbService adbService;
    private readonly Func<INetworkService> networkServiceFactory;

    private Dictionary<string, bool> deviceIdToAudioOnlyMap = [];
    private CancellationTokenSource? cts;
    private readonly Microsoft.UI.Dispatching.DispatcherQueue? dispatcher = App.MainWindow?.DispatcherQueue;

    // 密码缓存 + 密码对话框（字典所有权在新协作者内，保持普通 Dictionary 语义）
    private readonly ScrcpyPasswordCache passwordCacheStore;

    // scrcpy 进程启动/监控/停止
    private readonly ScrcpyProcessManager processManager;

    // scrcpy 命令行参数构建（无状态协作者）
    private readonly ScrcpyConfigBuilder configBuilder;

    // 设备选择阶段（匹配 / 弹窗 / 解锁 / 自动重连）
    private readonly ScrcpyDeviceSelector deviceSelector;

    // scrcpy 可执行文件路径解析与选择
    private readonly ScrcpyPathResolver pathResolver;

    public ScreenMirrorService(
        ILogger<ScreenMirrorService> logger,
        IUserSettingsService userSettingsService,
        IAdbService adbService,
        Func<INetworkService> networkServiceFactory)
    {
        this.logger = logger;
        this.userSettingsService = userSettingsService;
        this.adbService = adbService;
        this.networkServiceFactory = networkServiceFactory;
        passwordCacheStore = new ScrcpyPasswordCache(dispatcher);
        // 进程退出时反向通知主类（移除仅音频标记 / 置空 cts），以回调注入避免循环依赖
        processManager = new ScrcpyProcessManager(
            logger,
            dispatcher,
            deviceId => deviceIdToAudioOnlyMap.Remove(deviceId),
            ClearCtsIfCurrent);
        configBuilder = new ScrcpyConfigBuilder(logger, adbService, processManager.KillExistingProcess);
        // devices 与拆分前一致地指向 adbService.AdbDevices 同一集合实例
        deviceSelector = new ScrcpyDeviceSelector(logger, adbService, adbService.AdbDevices, passwordCacheStore, dispatcher);
        pathResolver = new ScrcpyPathResolver(logger, userSettingsService, adbService, dispatcher);
    }

    /// <summary>
    /// 仅当主类当前 cts 仍是该实例时置空（保持拆分前 ReferenceEquals 判定语义）。
    /// </summary>
    private void ClearCtsIfCurrent(CancellationTokenSource processCts)
    {
        if (ReferenceEquals(cts, processCts)) cts = null;
    }

    public async Task<bool> StartScrcpy(PairedDevice device, string? customArgs = null, string? iconPath = null)
    {
        logger.LogDebug("[调试] StartScrcpy 请求: deviceId={DeviceId} customArgs={CustomArgs} iconPath={IconPath}", device?.Id, customArgs, iconPath);
        try
        {
            // 额外记录设备会话和连接状态以便诊断
            logger.LogDebug("[调试] 设备信息: Name={Name} ConnectionStatus={ConnectionStatus}", device?.Name, device?.ConnectionStatus);
        }
        catch { }

        if (device == null)
        {
            logger.LogError("StartScrcpy 失败：设备为空");
            return false;
        }

        Process? process = null;
        CancellationTokenSource? processCts = null;

        var deviceSettings = device.DeviceSettings;
        try
        {
            var scrcpyPath = await pathResolver.ResolveAsync();
            if (scrcpyPath is null) return false;

            List<string> argBuilder = [];
            if (!string.IsNullOrEmpty(customArgs))
            {
                argBuilder.Add(customArgs);
            }

            var selectedDeviceSerial = await deviceSelector.ResolveDeviceSerialAsync(device, deviceSettings, argBuilder);
            // Validate that we have a selected device
            if (string.IsNullOrEmpty(selectedDeviceSerial)) return false;

            argBuilder.Add($"-s {selectedDeviceSerial}");

            // Build arguments for scrcpy with the selected device
            var (args, deviceSerial) = BuildScrcpyArguments(argBuilder, selectedDeviceSerial!, deviceSettings);

            cts?.Cancel();
            cts?.Dispose();
            processCts = new CancellationTokenSource();
            cts = processCts;

            if (!processManager.TryStartProcess(scrcpyPath, args, iconPath, processCts, out process))
            {
                return false;
            }

            // 传递设备名称和仅音频模式标志给监控方法
            // 判断是否为仅音频模式：检查设置或自定义参数中是否包含--no-video
            // 检查完整的参数列表，包括自定义参数和构建的参数
            var isAudioOnly = deviceSettings.DisableVideoForwarding ||
                              (customArgs?.Contains("--no-video") ?? false) ||
                              args.Contains("--no-video");
            // 存储设备ID到序列号的映射
            processManager.RegisterDevice(device.Id, deviceSerial);
            // 存储设备ID到仅音频模式的映射
            deviceIdToAudioOnlyMap[device.Id] = isAudioOnly;
            await processManager.StartProcessMonitoringAsync(process!, processCts, deviceSerial);
            return true;
        }
        catch (Exception ex)
        {
            logger.LogError("StartScrcpy 中出错：{ex}", ex);
            processCts?.Dispose();
            if (ReferenceEquals(cts, processCts)) cts = null;
            process?.Dispose();
            return false;
        }
    }

    private (string, string) BuildScrcpyArguments(List<string> args, string deviceSerial, IDeviceSettingsService settings)
        => configBuilder.Build(args, deviceSerial, settings);

    public Task<string> SelectScrcpyLocationClick() => pathResolver.PickLocationAsync();

    public void StopScrcpy(string deviceSerial) => processManager.StopScrcpy(deviceSerial);

    public void StopScrcpyByDeviceId(string deviceId) => processManager.StopScrcpyByDeviceId(deviceId);

    public bool IsAudioOnlyRunning(string deviceId)
    {
        // 检查设备是否有仅音频模式的scrcpy进程运行
        return deviceIdToAudioOnlyMap.TryGetValue(deviceId, out var isAudioOnly) &&
               isAudioOnly &&
               processManager.IsProcessRunningForDevice(deviceId);
    }

    public async Task ProcessAudioRequestAsync(PairedDevice device)
    {
        logger.LogDebug("收到音频转发请求");
        try
        {
            // 构建仅音频转发的 scrcpy 参数
            string customArgs = "--no-video --no-control";

            // 启动 scrcpy 仅音频转发
            bool success = await StartScrcpy(device, customArgs);

            // 构造响应
            var response = new
            {
                type = "MEDIA_CONTROL",
                action = "audioResponse",
                result = success ? "accepted" : "rejected"
            };
            string responseJson = JsonSerializer.Serialize(response);

            // 发送响应
            networkServiceFactory().SendMessage(device.Id, responseJson);

            logger.LogDebug("音频转发请求处理完成，结果：{result}", success ? "accepted" : "rejected");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "处理音频转发请求时出错");

            // 发送拒绝响应
            var errorResponse = new
            {
                type = "MEDIA_CONTROL",
                action = "audioResponse",
                result = "rejected"
            };
            string errorResponseJson = JsonSerializer.Serialize(errorResponse);
            networkServiceFactory().SendMessage(device.Id, errorResponseJson);
        }
    }

    public void Dispose()
    {
        processManager.Dispose();
        cts?.Dispose();
    }
}
