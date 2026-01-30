using NotifyRelay.Data.Contracts;
using NotifyRelay.Data.Models;
using NotifyRelay.Utils;

namespace NotifyRelay.Platforms.Desktop.Services;

public class DesktopftpService(ILogger<DesktopftpService> logger) : IftpService
{
    private readonly Dictionary<string, string> _mountedDevices = [];

    public async Task InitializeAsync(PairedDevice device, ftpServerInfo info)
    {
        logger.LogInformation("正在为设备 {DeviceName} 初始化 ftp 服务，IP：{IpAddress}，端口：{Port}",
            device.Name, info.IpAddress, info.Port);

        // 使用匿名登录构建ftpUri
        var ftpUri = $"ftp://{info.IpAddress}:{info.Port}/";

        logger.LogInformation("正在为设备 {DeviceName} 挂载 ftp", device.Name);

        ProcessExecutor.ExecuteProcess("gio", $"mount -s \"{ftpUri}\"");

        // 使用匿名登录，不需要密码
        var (exitCode, errorOutput) = await ExecuteProcessWithPasswordAsync("gio", $"mount \"{ftpUri}\"", string.Empty);

        if (exitCode != 0)
        {
            logger.LogError("为设备 {DeviceName} 挂载 ftp 失败：{Error}", device.Name, errorOutput);
            return;
        }

        _mountedDevices[device.Id] = ftpUri;
        logger.LogInformation("为设备 {DeviceName} 成功挂载 ftp", device.Name);
    }

    private static async Task<(int ExitCode, string ErrorOutput)> ExecuteProcessWithPasswordAsync(string fileName, string arguments, string password)
    {
        var psi = new ProcessStartInfo(fileName)
        {
            Arguments = arguments,
            CreateNoWindow = true,
            UseShellExecute = false,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            RedirectStandardInput = true
        };

        using var process = Process.Start(psi);
        if (process == null)
        {
            return (-1, "启动进程失败");
        }

        // Send password to stdin when prompted
        await process.StandardInput.WriteLineAsync(password);
        await process.StandardInput.FlushAsync();
        process.StandardInput.Close();

        await process.WaitForExitAsync();
        var errorOutput = await process.StandardError.ReadToEndAsync();

        return (process.ExitCode, errorOutput);
    }

    public void Remove(string deviceId)
    {
        if (!_mountedDevices.TryGetValue(deviceId, out var ftpUri))
        {
            logger.LogDebug("设备 {DeviceId} 未挂载", deviceId);
            return;
        }

        logger.LogInformation("正在卸载设备 {DeviceId} 的 ftp 挂载", deviceId);
        ProcessExecutor.ExecuteProcess("gio", $"mount -u \"{ftpUri}\"");
        _mountedDevices.Remove(deviceId);
    }
}
