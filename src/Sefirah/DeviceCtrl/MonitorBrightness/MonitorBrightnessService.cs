using System.Management;
using System.Text;
using NotifyRelay.Data.Contracts;

namespace NotifyRelay.DeviceCtrl.MonitorBrightness;

public class MonitorBrightnessService
{
    private readonly ILogger<MonitorBrightnessService> _logger;
    private readonly IGeneralSettingsService _generalSettingsService;
    private ManagementEventWatcher? _brightnessWatcher;
    private bool _isRunning;
    private List<MonitorInfo> _availableMonitors = [];

    public event EventHandler StatusChanged;

    public bool IsRunning => _isRunning;

    public MonitorBrightnessService(ILogger<MonitorBrightnessService> logger, IGeneralSettingsService generalSettingsService)
    {
        _logger = logger;
        _generalSettingsService = generalSettingsService;
    }

    public void StartSync()
    {
        if (_isRunning)
        {
            _logger.LogInformation("显示器亮度同步已经在运行");
            return;
        }

        if (string.IsNullOrEmpty(_generalSettingsService.ControlMyMonitorPath))
        {
            _logger.LogError("ControlMyMonitor路径未设置");
            return;
        }

        if (!File.Exists(_generalSettingsService.ControlMyMonitorPath))
        {
            _logger.LogError("在指定路径未找到ControlMyMonitor.exe");
            return;
        }

        try
        {
            LoadMonitors();
            StartBrightnessWatcher();

            // 启动时强制同步一次亮度，确保初始状态正确
            uint initialBrightness = GetCurrentSystemBrightness();
            _logger.LogInformation($"启动时强制同步亮度，初始亮度: {initialBrightness}%");
            SyncBrightness(initialBrightness);

            _isRunning = true;
            _generalSettingsService.EnableMonitorBrightnessSync = true;
            _logger.LogInformation("显示器亮度同步已启动");
            StatusChanged?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "启动显示器亮度同步失败");
        }
    }

    public void StopSync()
    {
        if (!_isRunning)
        {
            _logger.LogInformation("显示器亮度同步未运行");
            return;
        }

        try
        {
            StopBrightnessWatcher();
            _isRunning = false;
            _generalSettingsService.EnableMonitorBrightnessSync = false;
            _logger.LogInformation("显示器亮度同步已停止");
            StatusChanged?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "停止显示器亮度同步失败");
        }
    }

    public List<MonitorInfo> GetAvailableMonitors()
    {
        if (!_availableMonitors.Any())
        {
            LoadMonitors();
        }
        return _availableMonitors;
    }

    public void LoadMonitors()
    {
        if (string.IsNullOrEmpty(_generalSettingsService.ControlMyMonitorPath))
        {
            _logger.LogWarning("ControlMyMonitor路径未设置，无法加载显示器");
            return;
        }

        try
        {
            _availableMonitors = DetectMonitors();
            _logger.LogInformation($"已加载 {_availableMonitors.Count} 个显示器");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "加载显示器失败");
        }
    }

    private void StartBrightnessWatcher()
    {
        try
        {
            // 尝试使用 CIM 风格的查询，显式指定命名空间
            var query = new WqlEventQuery("SELECT * FROM WmiMonitorBrightnessEvent");
            _brightnessWatcher = new ManagementEventWatcher("root\\WMI", query.QueryString);
            _brightnessWatcher.EventArrived += OnBrightnessChanged;
            _brightnessWatcher.Start();
            _logger.LogInformation("亮度监视器已启动（使用CIM风格查询）");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "使用CIM风格查询启动亮度监视器失败");
            // 尝试使用更详细的配置
            try
            {
                // 尝试使用显式的 ManagementScope
                var scope = new ManagementScope("root\\WMI");
                scope.Connect();

                var query = new WqlEventQuery("SELECT * FROM WmiMonitorBrightnessEvent");
                _brightnessWatcher = new ManagementEventWatcher(scope, query);
                _brightnessWatcher.EventArrived += OnBrightnessChanged;
                _brightnessWatcher.Start();
                _logger.LogInformation("亮度监视器已启动（使用显式ManagementScope）");
            }
            catch (Exception ex2)
            {
                _logger.LogError(ex2, "使用显式ManagementScope启动亮度监视器失败");
                // 尝试使用轮询方式作为回退方案
                StartBrightnessPolling();
            }
        }
    }

    private void StartBrightnessPolling()
    {
        try
        {
            _logger.LogInformation("启动亮度轮询作为备用方案");
            // 启动一个后台线程进行亮度轮询
            Task.Run(async () =>
            {
                uint lastBrightness = 0;
                while (_isRunning)
                {
                    try
                    {
                        uint brightness = GetCurrentSystemBrightness();
                        if (brightness != lastBrightness)
                        {
                            lastBrightness = brightness;
                            _logger.LogInformation($"亮度已更改为 {brightness}%（轮询）");
                            SyncBrightness(brightness);
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "亮度轮询出错");
                    }
                    await Task.Delay(1000); // 每秒检查一次
                }
            });
            _logger.LogInformation("亮度轮询已启动");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "启动亮度轮询失败");
        }
    }

    private void StopBrightnessWatcher()
    {
        try
        {
            _brightnessWatcher?.Stop();
            _brightnessWatcher?.Dispose();
            _brightnessWatcher = null;
            _logger.LogInformation("亮度监视器已停止");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "停止亮度监视器失败");
        }
    }

    private void OnBrightnessChanged(object sender, EventArrivedEventArgs e)
    {
        try
        {
            // 尝试从事件参数中获取亮度值
            uint brightness = 0;
            if (e.NewEvent.Properties["Brightness"] != null)
            {
                var brightnessValue = e.NewEvent.Properties["Brightness"].Value;
                _logger.LogInformation($"事件中 Brightness 值: {brightnessValue}, 类型: {brightnessValue?.GetType().Name}");
                if (brightnessValue is uint eventBrightness)
                {
                    brightness = eventBrightness;
                }
                else if (brightnessValue is int intBrightness)
                {
                    brightness = (uint)intBrightness;
                }
                else if (brightnessValue is byte byteBrightness)
                {
                    brightness = byteBrightness;
                }
            }

            // 如果事件中没有获取到亮度值，则查询系统亮度
            if (brightness == 0)
            {
                brightness = GetCurrentSystemBrightness();
            }

            _logger.LogInformation($"亮度已更改为 {brightness}%");
            SyncBrightness(brightness);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "处理亮度变化事件失败");
        }
    }

    private uint GetCurrentSystemBrightness()
    {
        try
        {
            using var searcher = new ManagementObjectSearcher("root\\WMI", "SELECT * FROM WmiMonitorBrightness");
            using var collection = searcher.Get();
            _logger.LogInformation($"WmiMonitorBrightness 结果数量: {collection.Count}");

            foreach (var obj in collection)
            {
                try
                {
                    _logger.LogInformation($"对象属性: {string.Join(", ", obj.Properties.Cast<PropertyData>().Select(p => p.Name))}");
                    if (obj.Properties["CurrentBrightness"] != null)
                    {
                        var brightnessValue = obj.Properties["CurrentBrightness"].Value;
                        _logger.LogInformation($"CurrentBrightness 值: {brightnessValue}, 类型: {brightnessValue?.GetType().Name}");
                        if (brightnessValue is uint brightness)
                        {
                            return brightness;
                        }
                        else if (brightnessValue is int intBrightness)
                        {
                            return (uint)intBrightness;
                        }
                        else if (brightnessValue is byte byteBrightness)
                        {
                            return byteBrightness;
                        }
                    }
                    else
                    {
                        _logger.LogWarning("对象不包含 CurrentBrightness 属性");
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "处理单个亮度对象失败");
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "获取当前系统亮度失败");
        }
        return 0;
    }

    private void SyncBrightness(uint brightness)
    {
        if (string.IsNullOrEmpty(_generalSettingsService.ControlMyMonitorPath))
        {
            return;
        }

        try
        {
            var selectedMonitors = _generalSettingsService.SelectedMonitors;
            List<MonitorInfo> targetMonitors = [];

            if (!selectedMonitors.Any() || selectedMonitors.Contains("All"))
            {
                // 同步到所有显示器
                _logger.LogInformation($"同步亮度到所有显示器: {brightness}%");
                targetMonitors = _availableMonitors;
            }
            else
            {
                // 同步到选定的显示器
                _logger.LogInformation($"同步亮度到选定显示器: {brightness}%");
                foreach (var monitorId in selectedMonitors)
                {
                    var monitor = _availableMonitors.FirstOrDefault(m => m.Id == monitorId);
                    if (monitor != null)
                    {
                        targetMonitors.Add(monitor);
                    }
                }
            }

            // 构建复合命令，一次性执行所有亮度设置
            if (targetMonitors.Any())
            {
                StringBuilder commandBuilder = new();
                foreach (var monitor in targetMonitors)
                {
                    _logger.LogInformation($"同步亮度到显示器 {monitor.Name}: {brightness}%");
                    commandBuilder.Append($" /SetValue \"{monitor.DeviceName}\" 10 {brightness}");
                }

                string combinedArguments = commandBuilder.ToString().Trim();
                RunControlMyMonitor(combinedArguments);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "同步亮度失败");
        }
    }

    private List<MonitorInfo> DetectMonitors()
    {
        var monitors = new List<MonitorInfo>();
        var tempFile = Path.GetTempFileName() + ".txt";

        try
        {
            _logger.LogInformation($"开始检测显示器，临时文件: {tempFile}");
            RunControlMyMonitor($"/smonitors {tempFile}");

            if (File.Exists(tempFile))
            {
                _logger.LogInformation($"读取显示器列表文件: {tempFile}");
                var content = File.ReadAllText(tempFile);
                _logger.LogInformation($"显示器列表文件内容: {content}");

                var monitorSections = content.Split("\r\n\r\n", StringSplitOptions.RemoveEmptyEntries);
                _logger.LogInformation($"解析出 {monitorSections.Length} 个显示器部分");

                foreach (var section in monitorSections)
                {
                    var monitor = ParseMonitorSection(section);
                    if (monitor != null)
                    {
                        _logger.LogInformation($"发现显示器: {monitor.Name}, 设备名: {monitor.DeviceName}");
                        monitors.Add(monitor);
                    }
                }
            }
            else
            {
                _logger.LogError("临时文件不存在，无法读取显示器列表");
            }
        }
        finally
        {
            if (File.Exists(tempFile))
            {
                File.Delete(tempFile);
                _logger.LogInformation($"删除临时文件: {tempFile}");
            }
        }

        _logger.LogInformation($"检测完成，共发现 {monitors.Count} 个显示器");
        return monitors;
    }

    private MonitorInfo? ParseMonitorSection(string section)
    {
        var lines = section.Split("\r\n", StringSplitOptions.RemoveEmptyEntries);
        var monitor = new MonitorInfo();

        foreach (var line in lines)
        {
            var parts = line.Split(":", 2);
            if (parts.Length == 2)
            {
                var key = parts[0].Trim();
                var value = parts[1].Trim().Trim('"');

                switch (key)
                {
                    case "Monitor Name":
                        monitor.Name = value;
                        monitor.Id = "Monitor_" + value.Replace(" ", "_");
                        break;
                    case "Monitor Device Name":
                        monitor.DeviceName = value;
                        break;
                }
            }
        }

        return !string.IsNullOrEmpty(monitor.Name) && !string.IsNullOrEmpty(monitor.DeviceName) ? monitor : null;
    }

    private void RunControlMyMonitor(string arguments)
    {
        if (string.IsNullOrEmpty(_generalSettingsService.ControlMyMonitorPath))
        {
            _logger.LogError("ControlMyMonitor路径未设置");
            return;
        }

        if (!File.Exists(_generalSettingsService.ControlMyMonitorPath))
        {
            _logger.LogError($"ControlMyMonitor.exe不存在: {_generalSettingsService.ControlMyMonitorPath}");
            return;
        }

        try
        {
            _logger.LogInformation($"执行ControlMyMonitor命令: {_generalSettingsService.ControlMyMonitorPath} {arguments}");
            var processStartInfo = new ProcessStartInfo
            {
                FileName = _generalSettingsService.ControlMyMonitorPath,
                Arguments = arguments,
                WindowStyle = ProcessWindowStyle.Hidden,
                CreateNoWindow = true
            };

            using var process = Process.Start(processStartInfo);
            if (process != null)
            {
                bool exited = process.WaitForExit(5000);
                if (exited)
                {
                    _logger.LogInformation($"ControlMyMonitor命令执行完成，退出码: {process.ExitCode}");
                }
                else
                {
                    _logger.LogWarning("ControlMyMonitor命令执行超时");
                }
            }
            else
            {
                _logger.LogError("无法启动ControlMyMonitor进程");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "运行ControlMyMonitor失败，参数: {Arguments}", arguments);
        }
    }

    public void Dispose()
    {
        StopSync();
        _brightnessWatcher?.Dispose();
    }
}

public class MonitorInfo
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string DeviceName { get; set; } = string.Empty;
}
