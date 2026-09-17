using CommunityToolkit.WinUI;
using NotifyRelay.Data.Contracts;
using NotifyRelay.Data.Enums;
using NotifyRelay.Data.Models;
using NotifyRelay.Utils;

namespace NotifyRelay.Services.Media;

public class ScreenMirrorService : IScreenMirrorService, IDisposable
{
    // 主构造函数参数以字段形式保留（拆分后需在构造函数体内构造协作者，故改为显式构造函数）
    private readonly ILogger<ScreenMirrorService> logger;
    private readonly IUserSettingsService userSettingsService;
    private readonly IAdbService adbService;
    private readonly Func<INetworkService> networkServiceFactory;

    private readonly ObservableCollection<AdbDevice> devices;

    private Dictionary<string, bool> deviceIdToAudioOnlyMap = [];
    private CancellationTokenSource? cts;
    private readonly Microsoft.UI.Dispatching.DispatcherQueue? dispatcher = App.MainWindow?.DispatcherQueue;

    // 密码缓存 + 密码对话框（字典所有权在新协作者内，保持普通 Dictionary 语义）
    private readonly ScrcpyPasswordCache passwordCacheStore;

    // scrcpy 进程启动/监控/停止
    private readonly ScrcpyProcessManager processManager;

    // scrcpy 命令行参数构建（无状态协作者）
    private readonly ScrcpyConfigBuilder configBuilder;

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
        devices = adbService.AdbDevices;
        passwordCacheStore = new ScrcpyPasswordCache(dispatcher);
        // 进程退出时反向通知主类（移除仅音频标记 / 置空 cts），以回调注入避免循环依赖
        processManager = new ScrcpyProcessManager(
            logger,
            dispatcher,
            deviceId => deviceIdToAudioOnlyMap.Remove(deviceId),
            ClearCtsIfCurrent);
        configBuilder = new ScrcpyConfigBuilder(logger, adbService, processManager.KillExistingProcess);
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
            var scrcpyPath = await ResolveScrcpyPathAsync();
            if (scrcpyPath is null) return false;

            List<string> argBuilder = [];
            if (!string.IsNullOrEmpty(customArgs))
            {
                argBuilder.Add(customArgs);
            }

            var selectedDeviceSerial = await ResolveDeviceSerialAsync(device, deviceSettings, argBuilder);
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

    /// <summary>
    /// 解析 scrcpy 可执行文件路径；未找到时弹窗让用户选择，仍无效则返回 null（调用方短路返回 false）。
    /// </summary>
    private async Task<string?> ResolveScrcpyPathAsync()
    {
        var scrcpyPath = userSettingsService.GeneralSettingsService.ScrcpyPath;
        if (!File.Exists(scrcpyPath))
        {
            logger.LogError("未在路径找到 scrcpy：{ScrcpyPath}", scrcpyPath);
            var result = await dispatcher!.EnqueueAsync(async () =>
            {
                var dialog = new ContentDialog
                {
                    XamlRoot = App.MainWindow.Content!.XamlRoot,
                    Title = "ScrcpyNotFound".GetLocalizedResource(),
                    Content = "ScrcpyNotFoundDescription".GetLocalizedResource(),
                    PrimaryButtonText = "SelectLocation".GetLocalizedResource(),
                    DefaultButton = ContentDialogButton.Primary,
                    CloseButtonText = "Dismiss".GetLocalizedResource()
                };

                var dialogResult = await dialog.ShowAsync();
                if (dialogResult is ContentDialogResult.Primary)
                {
                    scrcpyPath = await SelectScrcpyLocationClick();
                    return !string.IsNullOrEmpty(scrcpyPath) && File.Exists(scrcpyPath);
                }
                return false;
            });

            if (!result) return null;
        }

        return scrcpyPath;
    }

    /// <summary>
    /// 设备选择阶段（原 StartScrcpy 主体）：匹配 ADB 设备 → 按偏好/弹窗选定 serial →
    /// 必要时解锁设备 → 必要时自动重连并重新匹配。
    /// 返回 null 表示应中止启动（调用方返回 false），等价于拆分前各处的 `return false`。
    /// </summary>
    private async Task<string?> ResolveDeviceSerialAsync(
        PairedDevice device,
        IDeviceSettingsService deviceSettings,
        List<string> argBuilder)
    {
        var devicePreferenceType = deviceSettings.ScrcpyDevicePreference;
        string? selectedDeviceSerial = null;

        // 根据已配对设备信息优先匹配 ADB 设备：
        // 1. 优先匹配 AndroidId == PairedDevice.Id（绑定映射）
        // 2. 否则通过型号匹配
        // 3. 在候选中优先选择 USB（有线）设备
        var adbOnlineDevices = devices.Where(d => d != null && d.IsOnline).ToList();
        var matchedDevices = adbOnlineDevices.Where(d => !string.IsNullOrEmpty(d.AndroidId) && d.AndroidId == device.Id).ToList();

        if (matchedDevices.Count == 0 && !string.IsNullOrEmpty(device.Model))
        {
            matchedDevices = adbOnlineDevices.Where(d => string.IsNullOrEmpty(d.AndroidId) &&
                !string.IsNullOrEmpty(d.Model) &&
                (device.Model.Equals(d.Model, StringComparison.OrdinalIgnoreCase) ||
                 device.Model.Contains(d.Model, StringComparison.OrdinalIgnoreCase) ||
                 d.Model.Contains(device.Model, StringComparison.OrdinalIgnoreCase)))
                .ToList();
        }

        var pairedDevices = matchedDevices.Count > 0 ? matchedDevices : devices.Where(d => d != null && d.Model == device.Model).ToList();

        // 如果存在多个候选 ADB 设备，弹窗选择并预选最优（USB 优先），而不是直接选择第一个
        if (pairedDevices.Count > 1)
        {
            var preferredSerial = pairedDevices.FirstOrDefault(d => d.Type == DeviceType.USB)?.Serial
                                  ?? pairedDevices.First().Serial;
            selectedDeviceSerial = await ShowDeviceSelectionDialog(pairedDevices, preferredSerial);
            if (string.IsNullOrEmpty(selectedDeviceSerial))
            {
                logger.LogWarning("用户在设备选择弹窗中取消或未选择设备");
                return null;
            }
        }
        else if (pairedDevices.Count > 0)
        {
            switch (devicePreferenceType)
            {
                case ScrcpyDevicePreferenceType.Usb:
                    // 优先选择已匹配设备中的 USB，否则从匹配列表中选择第一个
                    selectedDeviceSerial = pairedDevices.FirstOrDefault(d => d.Type == DeviceType.USB)?.Serial
                                        ?? pairedDevices.FirstOrDefault()?.Serial;
                    break;
                case ScrcpyDevicePreferenceType.Tcpip:
                    selectedDeviceSerial = pairedDevices.FirstOrDefault(d => d.Type == DeviceType.WIFI)?.Serial
                                        ?? pairedDevices.FirstOrDefault()?.Serial;
                    break;
                case ScrcpyDevicePreferenceType.Auto:
                    // 优先选择 USB，如果找到了 USB 且启用了 ADB TCP/IP 模式，则追加参数
                    var usbDevice = pairedDevices.FirstOrDefault(d => d.Type == DeviceType.USB);
                    if (usbDevice != null)
                    {
                        if (deviceSettings.AdbTcpipModeEnabled)
                        {
                            argBuilder.Add("--tcpip");
                        }
                        selectedDeviceSerial = usbDevice.Serial;
                    }
                    else
                    {
                        selectedDeviceSerial = pairedDevices.FirstOrDefault(d => d.Type == DeviceType.WIFI)?.Serial
                                            ?? pairedDevices.FirstOrDefault()?.Serial;
                    }
                    break;
                case ScrcpyDevicePreferenceType.AskEverytime:
                    // 计算首选序列号：优先 USB 设备
                    var preferred = pairedDevices.FirstOrDefault(d => d.Type == DeviceType.USB)?.Serial
                                    ?? pairedDevices.FirstOrDefault()?.Serial;
                    selectedDeviceSerial = await ShowDeviceSelectionDialog(pairedDevices, preferred);
                    if (string.IsNullOrEmpty(selectedDeviceSerial))
                    {
                        logger.LogWarning("未选择用于 scrcpy 的设备");
                        return null;
                    }
                    break;
            }
            var commands = deviceSettings.UnlockCommands?.Trim()
                .Split(['\n', '\r'], StringSplitOptions.RemoveEmptyEntries)
                .Select(c => c.Trim())
                .Where(c => !string.IsNullOrEmpty(c))
                .ToList();
            var adbDevice = pairedDevices.FirstOrDefault(d => d.Serial == selectedDeviceSerial);
            if (adbDevice is null || adbDevice.DeviceData == null) return null;

            if (commands?.Count > 0 && await adbService.IsLocked(adbDevice.DeviceData))
            {
                // Check if any command contains password placeholder
                var hasPasswordPlaceholder = commands.Any(c => c.Contains("%pwd%"));
                string? password = null;

                if (hasPasswordPlaceholder)
                {
                    // Only use password caching if timeout is greater than 0
                    var timeoutSeconds = deviceSettings.UnlockTimeout;
                    if (timeoutSeconds > 0)
                    {
                        // Try to get cached password first
                        password = passwordCacheStore.GetCachedPassword(device.Id, timeoutSeconds);
                    }

                    // If no cached password or caching is disabled, ask user for password
                    if (password is null)
                    {
                        password = await passwordCacheStore.ShowPasswordInputDialog();
                        if (password is null) return null;

                        // Only cache the password if timeout is greater than 0
                        if (timeoutSeconds > 0)
                        {
                            passwordCacheStore.CachePassword(device.Id, password, timeoutSeconds);
                        }
                    }

                    // Replace password placeholders with actual password
                    commands = commands.Select(c => c.Replace("%pwd%", password)).ToList();
                }

                adbService.UnlockDevice(adbDevice.DeviceData, commands);
            }
        }
        else if (deviceSettings.AdbTcpipModeEnabled && device.Session != null)
        {
            var connectedSessionIpAddress = device.Session.Socket.RemoteEndPoint?.ToString()?.Split(':')[0];
            if (await adbService.ConnectWireless(connectedSessionIpAddress))
            {
                selectedDeviceSerial = $"{connectedSessionIpAddress}:5555";
            }
        }
        else
        {
            logger.LogWarning("未找到匹配的ADB设备，尝试自动重连");

            // 尝试自动重连到设备的5555端口
            var reconnected = await adbService.TryAutoReconnectAsync(device);

            if (reconnected)
            {
                // 等待设备列表更新
                await Task.Delay(500);

                // 重新尝试匹配ADB设备
                var reconnectedAdbDevices = devices.Where(d => d.IsOnline).ToList();
                var rematchedDevices = reconnectedAdbDevices.Where(d => !string.IsNullOrEmpty(d.AndroidId) && d.AndroidId == device.Id).ToList();

                if (rematchedDevices.Count == 0 && !string.IsNullOrEmpty(device.Model))
                {
                    rematchedDevices = reconnectedAdbDevices.Where(d => string.IsNullOrEmpty(d.AndroidId) &&
                        !string.IsNullOrEmpty(d.Model) &&
                        (device.Model.Equals(d.Model, StringComparison.OrdinalIgnoreCase) ||
                         device.Model.Contains(d.Model, StringComparison.OrdinalIgnoreCase) ||
                         d.Model.Contains(device.Model, StringComparison.OrdinalIgnoreCase)))
                        .ToList();
                }

                if (rematchedDevices.Count > 0)
                {
                    var preferred = rematchedDevices.FirstOrDefault(d => d.Type == DeviceType.USB)?.Serial
                                    ?? rematchedDevices.FirstOrDefault()?.Serial;
                    selectedDeviceSerial = await ShowDeviceSelectionDialog(rematchedDevices, preferred);
                }
                else
                {
                    logger.LogWarning("自动重连后仍未找到匹配的ADB设备");
                    _ = dispatcher?.EnqueueAsync(async () =>
                    {
                        var dialog = new ContentDialog
                        {
                            XamlRoot = App.MainWindow.Content!.XamlRoot,
                            Title = "AdbDeviceOffline".GetLocalizedResource(),
                            Content = "AdbDeviceOfflineDescription".GetLocalizedResource(),
                            CloseButtonText = "Dismiss".GetLocalizedResource()
                        };
                        await dialog.ShowAsync();
                    });
                    return null;
                }
            }
            else
            {
                _ = dispatcher?.EnqueueAsync(async () =>
                {
                    var dialog = new ContentDialog
                    {
                        XamlRoot = App.MainWindow.Content!.XamlRoot,
                        Title = "AdbDeviceOffline".GetLocalizedResource(),
                        Content = "AdbDeviceOfflineDescription".GetLocalizedResource(),
                        CloseButtonText = "Dismiss".GetLocalizedResource()
                    };
                    await dialog.ShowAsync();
                });
                return null;
            }
        }

        return selectedDeviceSerial;
    }

    // 已移除单独的音频播放窗口实现；仅保留 scrcpy 进程管理

    private async Task<string?> ShowDeviceSelectionDialog(List<AdbDevice> onlineDevices, string? preferredSerial = null)
    {
        string? selectedDeviceSerial = null;

        await dispatcher!.EnqueueAsync(async () =>
        {
            var deviceOptions = new List<ComboBoxItem>();
            foreach (var device in onlineDevices)
            {
                var displayName = device.Model ?? "Unknown";
                var item = new ComboBoxItem
                {
                    Content = $"{displayName} - {device.Type} ({device.Serial})",
                    Tag = device.Serial
                };
                deviceOptions.Add(item);
            }

            var deviceSelector = new ComboBox
            {
                ItemsSource = deviceOptions,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                SelectedIndex = 0
            };

            // 如果提供了首选序列号，尝试设置为选中项
            if (!string.IsNullOrEmpty(preferredSerial))
            {
                for (int i = 0; i < deviceOptions.Count; i++)
                {
                    if ((deviceOptions[i].Tag as string) == preferredSerial)
                    {
                        deviceSelector.SelectedIndex = i;
                        logger.LogDebug("[调试] 在设备选择弹窗中预选设备：{Serial}", preferredSerial);
                        break;
                    }
                }
            }

            var dialog = new ContentDialog
            {
                XamlRoot = App.MainWindow.Content!.XamlRoot,
                Title = "SelectDevice".GetLocalizedResource(),
                Content = deviceSelector,
                PrimaryButtonText = "Start".GetLocalizedResource(),
                CloseButtonText = "Cancel".GetLocalizedResource(),
                DefaultButton = ContentDialogButton.Primary
            };

            var result = await dialog.ShowAsync();

            if (result is ContentDialogResult.Primary && deviceSelector.SelectedItem is ComboBoxItem selected)
            {
                selectedDeviceSerial = selected.Tag as string;
            }
        });

        return selectedDeviceSerial;
    }

    private (string, string) BuildScrcpyArguments(List<string> args, string deviceSerial, IDeviceSettingsService settings)
        => configBuilder.Build(args, deviceSerial, settings);

    public async Task<string> SelectScrcpyLocationClick()
    {
        var file = await PickerHelper.PickFileAsync();
        if (file?.Path is string path)
        {
            userSettingsService.GeneralSettingsService.ScrcpyPath = path;
            ToolPathHelper.TrySetCompanionTool(path, "adb.exe", p => userSettingsService.GeneralSettingsService.AdbPath = p);
            await adbService.StartAsync();
            return path;
        }
        return string.Empty;
    }

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
