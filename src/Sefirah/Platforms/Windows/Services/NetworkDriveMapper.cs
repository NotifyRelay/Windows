using NotifyRelay.Data.Contracts;
using NotifyRelay.Data.Models;
using NotifyRelay.Helpers;

namespace NotifyRelay.Platforms.Windows.Services;

/// <summary>
/// 网络磁盘映射服务，用于将ftp设备映射为资源管理器中的网络磁盘
/// </summary>
public class NetworkDriveMapper
{
    private readonly ILogger _logger;
    private readonly Dictionary<string, string> _mappedDrives = [];
    private readonly object _lock = new();

    /// <summary>
    /// 初始化NetworkDriveMapper类的新实例
    /// </summary>
    /// <param name="logger">日志记录器</param>
    public NetworkDriveMapper(ILogger logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// 使用PowerShell命令创建网络位置快捷方式
    /// </summary>
    /// <param name="device">设备信息</param>
    /// <param name="username">FTP用户名</param>
    /// <param name="password">FTP密码</param>
    /// <param name="ipAddress">FTP服务器IP</param>
    /// <param name="port">FTP端口</param>
    /// <returns>快捷方式路径</returns>
    public string MapftpDrive(PairedDevice device, string? username, string? password, string ipAddress, int port)
    {
        lock (_lock)
        {
            // 检查设备是否已映射
            if (_mappedDrives.TryGetValue(device.Id, out var existingDrive))
            {
                _logger.LogInformation("设备 {DeviceName} 已映射到网络位置: {DriveLetter}", device.Name, existingDrive);
                return existingDrive;
            }

            // 构建FTP URL，使用与 Android 端一致的凭据
            var ftpUrl = string.IsNullOrEmpty(username)
                ? $"ftp://{ipAddress}:{port}/"
                : $"ftp://{Uri.EscapeDataString(username)}:{Uri.EscapeDataString(password)}@{ipAddress}:{port}/";
            _logger.LogInformation("正在将设备 {DeviceName} 创建为网络位置，FTP URL: {FtpUrl}",
                device.Name, ftpUrl);

            try
            {
                // 获取当前用户的网络位置文件夹路径
                string networkShortcutsPath = Environment.GetFolderPath(Environment.SpecialFolder.NetworkShortcuts);
                _logger.LogDebug("网络位置文件夹路径: {NetworkShortcutsPath}", networkShortcutsPath);

                // 创建快捷方式文件名
                string shortcutFileName = $"{device.Name}.lnk";
                string shortcutPath = Path.Combine(networkShortcutsPath, shortcutFileName);
                _logger.LogDebug("快捷方式路径: {ShortcutPath}", shortcutPath);

                // 如果快捷方式已存在，先删除
                if (File.Exists(shortcutPath))
                {
                    _logger.LogInformation("快捷方式已存在，正在删除: {ShortcutPath}", shortcutPath);
                    File.Delete(shortcutPath);
                }

                // 使用PowerShell命令创建网络位置快捷方式
                string powerShellScript = "$shell = New-Object -ComObject WScript.Shell; $shortcut = $shell.CreateShortcut('" + shortcutPath.Replace("'", "''") + "'); $shortcut.TargetPath = '" + ftpUrl.Replace("'", "''") + "'; $shortcut.Save();";

                _logger.LogDebug("PowerShell脚本: {PowerShellScript}", powerShellScript);

                // 执行PowerShell命令
                var processStartInfo = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "powershell.exe",
                    Arguments = $"-NoProfile -ExecutionPolicy Bypass -Command {powerShellScript}",
                    CreateNoWindow = true,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };

                using var process = System.Diagnostics.Process.Start(processStartInfo);
                if (process == null)
                {
                    _logger.LogError("无法启动powershell.exe进程");
                    return string.Empty;
                }

                string output = process.StandardOutput.ReadToEnd();
                string error = process.StandardError.ReadToEnd();
                process.WaitForExit();

                if (process.ExitCode != 0)
                {
                    _logger.LogError("PowerShell命令执行失败，退出码: {ExitCode}, 输出: {Output}, 错误: {Error}",
                        process.ExitCode, output, error);
                    return string.Empty;
                }
                else
                {
                    _logger.LogInformation("PowerShell命令执行成功，输出: {Output}", output);
                }

                _logger.LogInformation("成功创建网络位置快捷方式: {ShortcutPath}", shortcutPath);

                // 记录映射关系
                _mappedDrives[device.Id] = shortcutPath;
                return shortcutPath;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "创建网络位置快捷方式失败，设备: {DeviceName}，FTP URL: {FtpUrl}",
                    device.Name, ftpUrl);
                return string.Empty;
            }
        }
    }

    /// <summary>
    /// 删除设备的网络位置快捷方式
    /// </summary>
    /// <param name="deviceId">设备ID</param>
    public void UnmapftpDrive(string deviceId)
    {
        lock (_lock)
        {
            if (_mappedDrives.TryGetValue(deviceId, out var shortcutPath))
            {
                try
                {
                    // 删除网络位置快捷方式
                    if (File.Exists(shortcutPath))
                    {
                        File.Delete(shortcutPath);
                        _mappedDrives.Remove(deviceId);
                        _logger.LogInformation("已删除设备 {DeviceId} 的网络位置快捷方式: {ShortcutPath}", deviceId, shortcutPath);
                    }
                    else
                    {
                        _logger.LogWarning("网络位置快捷方式不存在: {ShortcutPath}", shortcutPath);
                        _mappedDrives.Remove(deviceId);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "删除网络位置快捷方式时出错，路径: {ShortcutPath}", shortcutPath);
                }
            }
        }
    }

    public async Task ProcessFtpMessageAsync(PairedDevice device, string payload)
    {
        _logger.LogDebug("处理DATA_FTP消息，内容: {payload}", payload);
        try
        {
            using var doc = JsonDocument.Parse(payload);
            var root = doc.RootElement;

            var action = root.TryGetProperty("action", out var actionProp) ? actionProp.GetString() : string.Empty;
            _logger.LogDebug("ftp消息action: {action}", action);

            if (action == "started")
            {
                if (root.TryGetProperty("ipAddress", out var ipAddressProp))
                {
                    var ipAddress = ipAddressProp.GetString();
                    var port = root.TryGetProperty("port", out var portProp) ? portProp.GetInt32() : 22;

                    if (!string.IsNullOrEmpty(ipAddress))
                    {
                        if (device.SharedSecret == null || device.SharedSecret.Length == 0)
                        {
                            _logger.LogWarning("FTP 启动成功但设备 {DeviceName} 没有共享密钥，无法派生凭据", device.Name);
                            return;
                        }
                        // 从 sharedSecret 派生与 Android 端一致的 FTP 凭据
                        var creds = NotifyCryptoHelper.DeriveftpCredentials(device.SharedSecret);

                        await Task.Run(() =>
                        {
                            try
                            {
                                string mappedDrive = MapftpDrive(device, creds.Username, creds.Password, ipAddress, port);
                                if (!string.IsNullOrEmpty(mappedDrive))
                                {
                                    _logger.LogDebug("设备 {DeviceName} 已成功映射为网络磁盘，盘符：{MappedDrive}", device.Name, mappedDrive);
                                }
                                else
                                {
                                    _logger.LogWarning("网络磁盘映射失败，但未抛出异常，设备: {DeviceName}，IP: {IpAddress}，端口: {Port}",
                                        device.Name, ipAddress, port);
                                }
                            }
                            catch (Exception ex)
                            {
                                _logger.LogError(ex, "网络磁盘映射失败，设备: {DeviceName}，IP: {IpAddress}，端口: {Port}",
                                    device.Name, ipAddress, port);
                            }
                        });
                    }
                }
            }
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "解析ftp消息失败: {payload}", payload);
        }
    }

    /// <summary>
    /// 发送 FTP 控制命令到 Android 设备
    /// </summary>
    /// <param name="device">目标设备</param>
    /// <param name="action">操作类型：start 或 stop</param>
    /// <param name="username">用户名（仅 start 时需要）</param>
    /// <param name="password">密码（仅 start 时需要）</param>
    public void SendftpCommand(PairedDevice device, string action, string? username = null, string? password = null)
    {
        const string DeviceTypeAndroid = "android";

        bool IsRemoteDeviceAndroid(PairedDevice? device)
        {
            return device != null && (device.RemoteDeviceType?.Equals(DeviceTypeAndroid, StringComparison.OrdinalIgnoreCase) ?? true);
        }

        if (!IsRemoteDeviceAndroid(device))
        {
            _logger.LogWarning("FTP 命令被忽略：非 Android 设备 ({deviceType})", device.RemoteDeviceType);
            return;
        }

        try
        {
            var rawJson = JsonSerializer.Serialize(new
            {
                type = "DATA_FTP",
                action = action,
                username = action == "start" ? username : null,
                password = action == "start" ? password : null
            });
            var message = rawJson;
            if (message == null) return;
            var networkService = Ioc.Default.GetRequiredService<INetworkService>();
            networkService.SendMessage(device.Id, message);
            _logger.LogDebug("已发送 FTP 命令：action={action}, device={deviceName}", action, device.Name);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "发送 FTP 命令失败：{deviceName}", device.Name);
        }
    }
}
