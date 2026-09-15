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
    /// 启动 adbPath 指定的进程并等待其结束。
    /// 先并发开始读取 stdout/stderr，再等待进程退出，最后回收输出，
    /// 以避免输出缓冲区写满导致的管道死锁。
    /// 返回 null 表示进程未能启动（对齐原 Process.Start 返回 null 的情形）。
    /// </summary>
    public async Task<AdbProcessResult?> RunAsync(string adbPath, string arguments)
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

        var outputTask = process.StandardOutput.ReadToEndAsync();
        var errorTask = process.StandardError.ReadToEndAsync();

        await process.WaitForExitAsync();
        var output = await outputTask;
        var error = await errorTask;

        return new AdbProcessResult(process.ExitCode, output, error);
    }
}
