using AdvancedSharpAdbClient;
using AdvancedSharpAdbClient.DeviceCommands;
using AdvancedSharpAdbClient.Models;
using AdvancedSharpAdbClient.Receivers;

namespace NotifyRelay.Services.Adb;

/// <summary>
/// ADB 命令执行契约：设备枚举、shell 命令执行、无线连接、卸载包、读取设备信息文件。
/// </summary>
public interface IAdbCommandExecutor
{
    AdbClient AdbClient { get; }
    Task<IReadOnlyList<DeviceData>> GetDevicesAsync(CancellationToken ct = default);
    Task<string> ExecuteShellCommandAsync(DeviceData device, string command, CancellationToken ct = default);
    Task<bool> ConnectAsync(string? host, int port = 5555, CancellationToken ct = default);
    Task UninstallPackageAsync(DeviceData device, string packageName, CancellationToken ct = default);
    Task<string> ReadDeviceInfoFileAsync(DeviceData device, CancellationToken ct = default);
}

/// <summary>
/// 持有唯一的 <see cref="AdbClient"/> 实例，封装全部 adbClient 调用。
/// 不解析返回结果；不做业务判定；不碰 UI 线程。
/// 日志复用 <see cref="AdbService"/> 的日志类别，保证拆分后 Serilog 类别名与日志文本不变。
/// </summary>
public sealed class AdbCommandExecutor(ILogger<AdbService> logger) : IAdbCommandExecutor
{
    /// <summary>设备信息文件路径（用于读取 App 侧生成的 AndroidId 文本）。</summary>
    internal const string DeviceInfoPath =
        "cat /storage/emulated/0/Android/data/com.xzyht.notifyrelay/files/device_info.txt";

    /// <summary>目标应用包名。</summary>
    internal const string PackageName = "com.xzyht.notifyrelay";

    private readonly AdbClient adbClient = new();

    public AdbClient AdbClient => adbClient;

    public async Task<IReadOnlyList<DeviceData>> GetDevicesAsync(CancellationToken ct = default)
    {
        var devices = await adbClient.GetDevicesAsync(ct);
        return devices as IReadOnlyList<DeviceData> ?? devices.ToList();
    }

    public async Task<string> ExecuteShellCommandAsync(DeviceData device, string command, CancellationToken ct = default)
    {
        var receiver = new ConsoleOutputReceiver();
        await adbClient.ExecuteShellCommandAsync(device, command, receiver, ct);
        return receiver.ToString();
    }

    public async Task<bool> ConnectAsync(string? host, int port = 5555, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(host)) return false;

        try
        {
            var result = await adbClient.ConnectAsync(host, port, ct);
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

    public Task UninstallPackageAsync(DeviceData device, string packageName, CancellationToken ct = default)
        => adbClient.UninstallPackageAsync(device, packageName, ct);

    public Task<string> ReadDeviceInfoFileAsync(DeviceData device, CancellationToken ct = default)
        => ExecuteShellCommandAsync(device, DeviceInfoPath, ct);
}
