using System.Diagnostics;
using System.Management;
using System.Text;
using Microsoft.Extensions.Logging;
using NotifyRelay.Worker.Bridge;
using NotifyRelay.Worker.Configuration;

namespace NotifyRelay.Worker.Services;

public class MonitorInfo
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string DeviceName { get; set; } = string.Empty;
}

public class MonitorBrightnessService
{
    private readonly ILogger _logger;
    private readonly WorkerConfiguration _config;
    private readonly PipeServer _pipeServer;
    private ManagementEventWatcher? _brightnessWatcher;
    private bool _isRunning;
    private List<MonitorInfo> _availableMonitors = [];

    public bool IsRunning => _isRunning;

    public MonitorBrightnessService(ILogger logger, WorkerConfiguration config, PipeServer pipeServer)
    {
        _logger = logger;
        _config = config;
        _pipeServer = pipeServer;
    }

    public void StartSync()
    {
        if (_isRunning) return;

        if (string.IsNullOrEmpty(_config.ControlMyMonitorPath))
        {
            _logger.LogError("ControlMyMonitor path not set");
            return;
        }

        if (!File.Exists(_config.ControlMyMonitorPath))
        {
            _logger.LogError("ControlMyMonitor.exe not found at {Path}", _config.ControlMyMonitorPath);
            return;
        }

        try
        {
            LoadMonitors();
            StartBrightnessWatcher();

            uint initialBrightness = GetCurrentSystemBrightness();
            _logger.LogInformation("Initial brightness: {Brightness}%", initialBrightness);
            SyncBrightness(initialBrightness);

            _isRunning = true;
            _logger.LogInformation("Monitor brightness sync started");
            _ = NotifyStatusAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to start monitor brightness sync");
        }
    }

    public void StopSync()
    {
        if (!_isRunning) return;

        try
        {
            StopBrightnessWatcher();
            _isRunning = false;
            _logger.LogInformation("Monitor brightness sync stopped");
            _ = NotifyStatusAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to stop monitor brightness sync");
        }
    }

    public List<MonitorInfo> GetAvailableMonitors()
    {
        if (_availableMonitors.Count == 0)
            LoadMonitors();
        return _availableMonitors;
    }

    public void LoadMonitors()
    {
        if (string.IsNullOrEmpty(_config.ControlMyMonitorPath))
        {
            _logger.LogWarning("ControlMyMonitor path not set");
            return;
        }

        try
        {
            _availableMonitors = DetectMonitors();
            _logger.LogInformation("Loaded {Count} monitors", _availableMonitors.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load monitors");
        }
    }

    private void StartBrightnessWatcher()
    {
        try
        {
            var query = new WqlEventQuery("SELECT * FROM WmiMonitorBrightnessEvent");
            _brightnessWatcher = new ManagementEventWatcher("root\\WMI", query.QueryString);
            _brightnessWatcher.EventArrived += OnBrightnessChanged;
            _brightnessWatcher.Start();
            _logger.LogInformation("Brightness watcher started");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to start brightness watcher via CIM query");
            try
            {
                var scope = new ManagementScope("root\\WMI");
                scope.Connect();
                var query = new WqlEventQuery("SELECT * FROM WmiMonitorBrightnessEvent");
                _brightnessWatcher = new ManagementEventWatcher(scope, query);
                _brightnessWatcher.EventArrived += OnBrightnessChanged;
                _brightnessWatcher.Start();
                _logger.LogInformation("Brightness watcher started with explicit scope");
            }
            catch (Exception ex2)
            {
                _logger.LogError(ex2, "Failed to start brightness watcher, falling back to polling");
                StartBrightnessPolling();
            }
        }
    }

    private void StartBrightnessPolling()
    {
        _ = Task.Run(async () =>
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
                        _logger.LogInformation("Brightness changed to {Brightness}% (polling)", brightness);
                        SyncBrightness(brightness);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Brightness polling error");
                }
                await Task.Delay(1000);
            }
        });
    }

    private void StopBrightnessWatcher()
    {
        try
        {
            _brightnessWatcher?.Stop();
            _brightnessWatcher?.Dispose();
            _brightnessWatcher = null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to stop brightness watcher");
        }
    }

    private void OnBrightnessChanged(object sender, EventArrivedEventArgs e)
    {
        try
        {
            uint brightness = 0;
            if (e.NewEvent.Properties["Brightness"]?.Value is uint eventBrightness)
                brightness = eventBrightness;
            else if (e.NewEvent.Properties["Brightness"]?.Value is int intBrightness)
                brightness = (uint)intBrightness;
            else if (e.NewEvent.Properties["Brightness"]?.Value is byte byteBrightness)
                brightness = byteBrightness;

            if (brightness == 0)
                brightness = GetCurrentSystemBrightness();

            _logger.LogInformation("Brightness changed to {Brightness}%", brightness);
            SyncBrightness(brightness);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to handle brightness change event");
        }
    }

    private uint GetCurrentSystemBrightness()
    {
        try
        {
            using var searcher = new ManagementObjectSearcher("root\\WMI", "SELECT * FROM WmiMonitorBrightness");
            using var collection = searcher.Get();

            foreach (var obj in collection)
            {
                if (obj.Properties["CurrentBrightness"]?.Value is uint brightness)
                    return brightness;
                if (obj.Properties["CurrentBrightness"]?.Value is int intBrightness)
                    return (uint)intBrightness;
                if (obj.Properties["CurrentBrightness"]?.Value is byte byteBrightness)
                    return byteBrightness;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get current system brightness");
        }
        return 0;
    }

    private void SyncBrightness(uint brightness)
    {
        if (string.IsNullOrEmpty(_config.ControlMyMonitorPath)) return;

        try
        {
            var selectedMonitors = _config.SelectedMonitors;
            List<MonitorInfo> targetMonitors = [];

            if (selectedMonitors.Count == 0 || selectedMonitors.Contains("All"))
            {
                targetMonitors = _availableMonitors;
            }
            else
            {
                foreach (var monitorId in selectedMonitors)
                {
                    var monitor = _availableMonitors.FirstOrDefault(m => m.Id == monitorId);
                    if (monitor != null) targetMonitors.Add(monitor);
                }
            }

            if (targetMonitors.Count > 0)
            {
                var commandBuilder = new StringBuilder();
                foreach (var monitor in targetMonitors)
                {
                    commandBuilder.Append($" /SetValue \"{monitor.DeviceName}\" 10 {brightness}");
                }
                RunControlMyMonitor(commandBuilder.ToString().Trim());
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to sync brightness");
        }
    }

    private List<MonitorInfo> DetectMonitors()
    {
        var monitors = new List<MonitorInfo>();
        var tempFile = Path.GetTempFileName() + ".txt";

        try
        {
            RunControlMyMonitor($"/smonitors {tempFile}");

            if (File.Exists(tempFile))
            {
                var content = File.ReadAllText(tempFile);
                var monitorSections = content.Split("\r\n\r\n", StringSplitOptions.RemoveEmptyEntries);

                foreach (var section in monitorSections)
                {
                    var monitor = ParseMonitorSection(section);
                    if (monitor != null) monitors.Add(monitor);
                }
            }
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }

        return monitors;
    }

    private static MonitorInfo? ParseMonitorSection(string section)
    {
        var lines = section.Split("\r\n", StringSplitOptions.RemoveEmptyEntries);
        var monitor = new MonitorInfo();

        foreach (var line in lines)
        {
            var parts = line.Split(":", 2);
            if (parts.Length != 2) continue;

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

        return !string.IsNullOrEmpty(monitor.Name) && !string.IsNullOrEmpty(monitor.DeviceName) ? monitor : null;
    }

    private void RunControlMyMonitor(string arguments)
    {
        if (string.IsNullOrEmpty(_config.ControlMyMonitorPath)) return;
        if (!File.Exists(_config.ControlMyMonitorPath)) return;

        try
        {
            var processStartInfo = new ProcessStartInfo
            {
                FileName = _config.ControlMyMonitorPath,
                Arguments = arguments,
                WindowStyle = ProcessWindowStyle.Hidden,
                CreateNoWindow = true
            };

            using var process = Process.Start(processStartInfo);
            process?.WaitForExit(5000);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to run ControlMyMonitor");
        }
    }

    private async Task NotifyStatusAsync()
    {
        await _pipeServer.SendEventAsync(IpcMessage.CreateEvent("brightness", "statusChanged", new
        {
            isRunning = _isRunning,
            monitors = _availableMonitors
        }));
    }
}
