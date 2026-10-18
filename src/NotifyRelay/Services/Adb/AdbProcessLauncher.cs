namespace NotifyRelay.Services.Adb;

/// <summary>
/// 外部 adb.exe 进程的执行结果（退出码 + 完整 stdout/stderr）。
/// </summary>
public sealed record AdbProcessResult(int ExitCode, string StandardOutput, string StandardError);

/// <summary>
/// 启动外部 adb.exe 进程并完整回收 stdout/stderr。
/// 只负责进程启动与输出回收，不解析 adb 输出语义，不读取配置（adbPath 由调用方传入）。
/// </summary>
public sealed class AdbProcessLauncher
{
    /// <summary>
    /// 启动 adbPath 指定的进程并等待其结束；取消（含调用方超时）时强制终止该进程，不会无限等待。
    /// 先并发开始读取 stdout/stderr，再等待进程退出，最后回收输出，
    /// 以避免输出缓冲区写满导致的管道死锁。
    /// 返回 null 表示进程未能启动（对齐原 Process.Start 返回 null 的情形），
    /// 或因取消/超时被强制终止（此时输出不可用，不再返回退出码）。
    /// </summary>
    public async Task<AdbProcessResult?> RunAsync(string adbPath, string arguments, CancellationToken cancellationToken = default)
    {
        var processInfo = new ProcessStartInfo
        {
            FileName = adbPath,
            Arguments = arguments,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = Process.Start(processInfo);
        if (process == null)
        {
            return null;
        }

        var outputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var errorTask = process.StandardError.ReadToEndAsync(cancellationToken);

        try
        {
            await process.WaitForExitAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            // 取消或超时：强制结束 adb 进程，避免任务永久挂起
            try
            {
                if (!process.HasExited)
                {
                    // 只终止卡住的 adb 客户端，不牵连 adb server（entireProcessTree 可能误杀常驻服务）
                    process.Kill();
                }
            }
            catch (Exception)
            {
                // 进程可能已自行退出，忽略终止失败
            }

            // 进程被终止后管道关闭，读取任务会自行结束；此处仅吞掉其取消/异常，避免未观察异常
            try
            {
                await Task.WhenAll(outputTask, errorTask);
            }
            catch (Exception)
            {
                // 已取消或管道中断，输出不可用
            }
            return null;
        }

        var output = await outputTask;
        var error = await errorTask;

        return new AdbProcessResult(process.ExitCode, output, error);
    }
}
