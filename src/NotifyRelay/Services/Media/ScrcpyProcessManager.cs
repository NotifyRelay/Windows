using System.Text;
using CommunityToolkit.WinUI;
using Windows.ApplicationModel.DataTransfer;

namespace NotifyRelay.Services.Media;

/// <summary>
/// scrcpy 进程的启动、监控与停止（由 ScreenMirrorService 整体搬入）。
/// 持有 scrcpyProcesses 与 deviceIdToSerialMap 字典所有权；
/// 不构建参数、不弹设备/密码对话框、不知道密码。
/// 需要通知主类（audioOnly 标记 / cts 置空）时通过构造注入的 Action 回调，
/// 不反向持有 ScreenMirrorService 引用，避免循环依赖。
/// </summary>
internal sealed class ScrcpyProcessManager : IDisposable
{
    private readonly ILogger logger;
    private readonly Microsoft.UI.Dispatching.DispatcherQueue? dispatcher;

    // 进程退出时通知主类移除该设备的仅音频标记（deviceId -> 主类 deviceIdToAudioOnlyMap）
    private readonly Action<string> onDeviceAudioStateRemoved;

    // 进程退出/启动失败时通知主类置空 cts（仅当主类当前 cts 仍是该实例）
    private readonly Action<CancellationTokenSource> clearCtsIfCurrent;

    private Dictionary<string, Process> scrcpyProcesses = [];
    private Dictionary<string, string> deviceIdToSerialMap = [];

    public ScrcpyProcessManager(
        ILogger logger,
        Microsoft.UI.Dispatching.DispatcherQueue? dispatcher,
        Action<string> onDeviceAudioStateRemoved,
        Action<CancellationTokenSource> clearCtsIfCurrent)
    {
        this.logger = logger;
        this.dispatcher = dispatcher;
        this.onDeviceAudioStateRemoved = onDeviceAudioStateRemoved;
        this.clearCtsIfCurrent = clearCtsIfCurrent;
    }

    /// <summary>
    /// 创建并启动 scrcpy 进程；失败时按原有分支释放进程对象与 CancellationTokenSource，并返回 false。
    /// </summary>
    public bool TryStartProcess(
        string scrcpyPath,
        string args,
        string? iconPath,
        CancellationTokenSource processCts,
        out Process? process)
    {
        process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = scrcpyPath,
                Arguments = args,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            },
            EnableRaisingEvents = true
        };

        if (!string.IsNullOrEmpty(iconPath))
        {
            process.StartInfo.EnvironmentVariables["SCRCPY_ICON_PATH"] = iconPath;
            logger.LogInformation($"正在使用自定义 scrcpy 图标：{iconPath}");
        }

        bool started;
        try
        {
            started = process.Start();
            logger.LogDebug("[调试] scrcpy 进程 Start() 返回: {Started}", started);
        }
        catch (Exception ex)
        {
            logger.LogError($"启动 scrcpy 失败：{ex.Message}", ex);
            process?.Dispose();
            processCts.Dispose();
            clearCtsIfCurrent(processCts);
            return false;
        }

        if (!started)
        {
            logger.LogError("启动 scrcpy 进程失败");
            process?.Dispose();
            processCts.Dispose();
            clearCtsIfCurrent(processCts);
            return false;
        }

        return true;
    }

    /// <summary>记录 deviceId -> serial 映射。</summary>
    public void RegisterDevice(string deviceId, string deviceSerial)
    {
        // 存储设备ID到序列号的映射
        deviceIdToSerialMap[deviceId] = deviceSerial;
    }

    public async Task StartProcessMonitoringAsync(Process process, CancellationTokenSource processCts, string deviceSerial)
    {
        var errorOutput = new StringBuilder();

        process.OutputDataReceived += (_, e) =>
        {
            if (!string.IsNullOrEmpty(e.Data))
                logger.LogInformation($"scrcpy：{e.Data}");
        };

        process.ErrorDataReceived += (_, e) =>
        {
            if (!string.IsNullOrEmpty(e.Data))
            {
                logger.LogError($"scrcpy 错误：{e.Data}");
                lock (errorOutput)
                {
                    errorOutput.AppendLine(e.Data);
                }
            }
        };

        process.Exited += (_, _) =>
        {
            logger.LogInformation("scrcpy 进程已终止");
        };

        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        logger.LogInformation("scrcpy 进程已启动（pid：{pid}）", process.Id);

        // 不再创建独立窗口，仅记录 scrcpy 进程
        scrcpyProcesses[deviceSerial] = process;

        _ = Task.Run(async () =>
        {
            try
            {
                await process.WaitForExitAsync(processCts.Token);
                logger.LogInformation("scrcpy 进程退出，代码：{exitCode}", process.ExitCode);

                // 仅当退出码不是0、2或-1时显示错误
                // 0: 正常退出
                // 2: 用户主动关闭窗口
                // -1: 显式终止进程（如我们调用Kill()时）
                if (process.ExitCode != 0 && process.ExitCode != 2 && process.ExitCode != -1)
                {
                    string errorMessage;
                    lock (errorOutput)
                    {
                        errorMessage = $"Scrcpy 进程以代码 {process.ExitCode} 退出\n\n错误输出：\n{errorOutput.ToString().TrimEnd()}";
                    }
                    logger.LogError("scrcpy 失败：{error}", errorMessage);

                    await dispatcher!.EnqueueAsync(async () =>
                    {
                        var scrollViewer = new ScrollViewer
                        {
                            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
                            MaxHeight = 300,
                            Content = new TextBlock
                            {
                                Text = errorMessage,
                                IsTextSelectionEnabled = true,
                                TextWrapping = TextWrapping.Wrap
                            }
                        };

                        var errorDialog = new ContentDialog
                        {
                            XamlRoot = App.MainWindow.Content!.XamlRoot,
                            Title = "ScrcpyErrorTitle".GetLocalizedResource(),
                            Content = scrollViewer,
                            CloseButtonText = "Dismiss".GetLocalizedResource(),
                            SecondaryButtonText = "CopyError".GetLocalizedResource()
                        };

                        var result = await errorDialog.ShowAsync();
                        if (result is ContentDialogResult.Secondary)
                        {
                            var dataPackage = new DataPackage();
                            dataPackage.SetText(errorMessage);
                            Clipboard.SetContent(dataPackage);
                            logger.LogInformation("scrcpy 错误输出已复制到剪贴板");
                        }
                    });
                }
            }
            catch (Exception ex)
            {
                if (ex is not OperationCanceledException)
                {
                    logger.LogError("监控 scrcpy 进程时出错：{ex}", ex);
                }
            }
            finally
            {
                process.Dispose();
                scrcpyProcesses.Remove(deviceSerial);
                // 移除设备ID到序列号的映射
                var deviceId = deviceIdToSerialMap.FirstOrDefault(x => x.Value == deviceSerial).Key;
                if (deviceId != null)
                {
                    deviceIdToSerialMap.Remove(deviceId);
                    // 移除设备ID到仅音频模式的映射（由主类持有）
                    onDeviceAudioStateRemoved(deviceId);
                }

                processCts.Dispose();
                clearCtsIfCurrent(processCts);
            }
        }, processCts.Token);
    }

    /// <summary>
    /// 终止指定设备上已存在的 scrcpy 进程（参数构建阶段使用）。
    /// </summary>
    public void KillExistingProcess(string deviceSerial)
    {
        if (scrcpyProcesses.Count == 0) return;

        // Check for existing processes for this device and terminate them
        // when virtual display is not enabled
        if (scrcpyProcesses.TryGetValue(deviceSerial, out var existingProcess))
        {
            try
            {
                if (!existingProcess.HasExited)
                {
                    existingProcess.Kill();
                }
                scrcpyProcesses.Remove(deviceSerial);
            }
            catch (Exception ex)
            {
                logger.LogError($"终止现有进程失败：{ex.Message}", ex);
            }
        }
    }

    public void StopScrcpy(string deviceSerial)
    {
        if (scrcpyProcesses.TryGetValue(deviceSerial, out var process))
        {
            try
            {
                if (!process.HasExited)
                {
                    process.Kill();
                }
                scrcpyProcesses.Remove(deviceSerial);
            }
            catch (Exception ex)
            {
                logger.LogError($"停止 scrcpy 进程失败：{ex.Message}", ex);
            }
        }
    }

    public void StopScrcpyByDeviceId(string deviceId)
    {
        if (deviceIdToSerialMap.TryGetValue(deviceId, out var deviceSerial))
        {
            StopScrcpy(deviceSerial);
        }
    }

    /// <summary>
    /// 该设备是否仍有存活的 scrcpy 进程（对应原有 deviceIdToSerialMap + scrcpyProcesses 双重查询）。
    /// </summary>
    public bool IsProcessRunningForDevice(string deviceId)
        => deviceIdToSerialMap.TryGetValue(deviceId, out var deviceSerial) &&
           scrcpyProcesses.TryGetValue(deviceSerial, out var process) &&
           !process.HasExited;

    /// <summary>终止并释放全部 scrcpy 进程。</summary>
    public void Dispose()
    {
        foreach (var kvp in scrcpyProcesses.ToList())
        {
            try
            {
                if (!kvp.Value.HasExited)
                {
                    kvp.Value.Kill();
                }
                kvp.Value.Dispose();
            }
            catch (Exception ex)
            {
                logger.LogError($"释放 scrcpy 进程失败：{ex.Message}", ex);
            }
        }
        scrcpyProcesses.Clear();
    }
}
