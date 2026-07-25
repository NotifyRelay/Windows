using Microsoft.Extensions.Logging;
using NotifyRelay.Worker.Configuration;
using NotifyRelay.Worker.Services;

namespace NotifyRelay.Worker.Bridge;

public class ServiceHost
{
    private readonly WorkerConfiguration _config;
    private readonly PipeServer _pipeServer;
    private readonly ILogger<ServiceHost> _logger;

    public DeepSeekBalanceService? DeepSeekBalance { get; private set; }
    public MonitorBrightnessService? MonitorBrightness { get; private set; }
    public DynamicLightingService? DynamicLighting { get; private set; }

    public ServiceHost(WorkerConfiguration config, PipeServer pipeServer, ILogger<ServiceHost> logger)
    {
        _config = config;
        _pipeServer = pipeServer;
        _logger = logger;
    }

    public void Initialize()
    {
        var deepSeekLogger = _logger;
        DeepSeekBalance = new DeepSeekBalanceService(deepSeekLogger, _config, _pipeServer);

        var brightnessLogger = _logger;
        MonitorBrightness = new MonitorBrightnessService(brightnessLogger, _config, _pipeServer);

        var lightingLogger = _logger;
        DynamicLighting = new DynamicLightingService(lightingLogger, _config, _pipeServer);

        _logger.LogInformation("All services initialized");
    }

    public void Cleanup()
    {
        DynamicLighting?.Cleanup();
        DeepSeekBalance?.StopPolling();
        MonitorBrightness?.StopSync();
        _logger.LogInformation("All services cleaned up");
    }

    public async Task<IpcMessage?> ExecuteCommandAsync(IpcMessage message)
    {
        var service = message.Service;
        var method = message.Method;

        try
        {
            return (service, method) switch
            {
                ("deepseek", "startPolling") => await HandleDeepSeekStartAsync(message),
                ("deepseek", "stopPolling") => HandleDeepSeekStop(message),
                ("deepseek", "fetchBalance") => await HandleDeepSeekFetchAsync(message),
                ("deepseek", "clearHistory") => HandleDeepSeekClearHistory(message),
                ("brightness", "startSync") => HandleBrightnessStart(message),
                ("brightness", "stopSync") => HandleBrightnessStop(message),
                ("brightness", "detectMonitors") => HandleBrightnessDetect(message),
                ("lighting", "initialize") => HandleLightingInitialize(message),
                ("lighting", "setColor") => HandleLightingSetColor(message),
                ("lighting", "setEffect") => HandleLightingSetEffect(message),
                ("lighting", "startAutoRGB") => HandleLightingStartAutoRGB(message),
                ("lighting", "stopAutoRGB") => HandleLightingStopAutoRGB(message),
                ("lighting", "setBrightness") => HandleLightingSetBrightness(message),
                ("lighting", "cleanup") => HandleLightingCleanup(message),
                ("lighting", "getDevices") => HandleLightingGetDevices(message),
                _ => IpcMessage.CreateResponse(message.Id ?? "unknown", false)
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error executing command: {Service}/{Method}", service, method);
            return IpcMessage.CreateResponse(message.Id ?? "unknown", false);
        }
    }

    public Task PushConfigAsync(Dictionary<string, object?> config)
    {
        _config.ApplyConfig(config);
        _logger.LogInformation("Configuration updated from main app");
        return Task.CompletedTask;
    }

    private async Task<IpcMessage?> HandleDeepSeekStartAsync(IpcMessage message)
    {
        if (DeepSeekBalance == null)
            return IpcMessage.CreateResponse(message.Id!, false);

        var token = _config.DeepSeekApiToken;
        if (message.Params.HasValue)
        {
            var p = message.Params.Value;
            if (p.TryGetProperty("token", out var t))
                token = t.GetString();
            if (p.TryGetProperty("interval", out var i))
                _config.DeepSeekBalancePollingInterval = i.GetInt32();
        }

        DeepSeekBalance.StartPolling();
        return IpcMessage.CreateResponse(message.Id!, true);
    }

    private IpcMessage? HandleDeepSeekStop(IpcMessage message)
    {
        DeepSeekBalance?.StopPolling();
        return IpcMessage.CreateResponse(message.Id!, true);
    }

    private async Task<IpcMessage?> HandleDeepSeekFetchAsync(IpcMessage message)
    {
        if (DeepSeekBalance == null)
            return IpcMessage.CreateResponse(message.Id!, false);

        var balance = await DeepSeekBalance.FetchBalanceAsync();
        return IpcMessage.CreateResponse(message.Id!, true, new { balance });
    }

    private IpcMessage? HandleDeepSeekClearHistory(IpcMessage message)
    {
        DeepSeekBalance?.ClearHistory();
        return IpcMessage.CreateResponse(message.Id!, true);
    }

    private IpcMessage? HandleBrightnessStart(IpcMessage message)
    {
        if (MonitorBrightness == null)
            return IpcMessage.CreateResponse(message.Id!, false);

        MonitorBrightness.StartSync();
        return IpcMessage.CreateResponse(message.Id!, true);
    }

    private IpcMessage? HandleBrightnessStop(IpcMessage message)
    {
        MonitorBrightness?.StopSync();
        return IpcMessage.CreateResponse(message.Id!, true);
    }

    private IpcMessage? HandleBrightnessDetect(IpcMessage message)
    {
        if (MonitorBrightness == null)
            return IpcMessage.CreateResponse(message.Id!, false);

        var monitors = MonitorBrightness.GetAvailableMonitors();
        return IpcMessage.CreateResponse(message.Id!, true, new { monitors });
    }

    private IpcMessage? HandleLightingInitialize(IpcMessage message)
    {
        DynamicLighting?.Initialize();
        return IpcMessage.CreateResponse(message.Id!, true);
    }

    private IpcMessage? HandleLightingSetColor(IpcMessage message)
    {
        if (DynamicLighting == null || !message.Params.HasValue)
            return IpcMessage.CreateResponse(message.Id!, false);

        var p = message.Params.Value;
        var color = p.GetProperty("color").GetString();
        var brightness = p.TryGetProperty("brightness", out var b) ? b.GetDouble() : _config.DynamicLightingBrightness;

        if (color != null)
        {
            DynamicLighting.Brightness = brightness;
            DynamicLighting.SetColorFromString(color);
        }

        return IpcMessage.CreateResponse(message.Id!, true);
    }

    private IpcMessage? HandleLightingSetEffect(IpcMessage message)
    {
        if (DynamicLighting == null || !message.Params.HasValue)
            return IpcMessage.CreateResponse(message.Id!, false);

        var p = message.Params.Value;
        var effect = p.GetProperty("effect").GetString();

        switch (effect)
        {
            case "rainbow":
                DynamicLighting.StartRainbowEffect();
                break;
            case "blink":
                DynamicLighting.StartBlinkEffect(DynamicLighting.CurrentColor);
                break;
            case "none":
                DynamicLighting.StopAllEffects();
                break;
        }

        return IpcMessage.CreateResponse(message.Id!, true);
    }

    private IpcMessage? HandleLightingStartAutoRGB(IpcMessage message)
    {
        DynamicLighting?.StartAutoRGB();
        return IpcMessage.CreateResponse(message.Id!, true);
    }

    private IpcMessage? HandleLightingStopAutoRGB(IpcMessage message)
    {
        DynamicLighting?.StopAutoRGB();
        return IpcMessage.CreateResponse(message.Id!, true);
    }

    private IpcMessage? HandleLightingSetBrightness(IpcMessage message)
    {
        if (DynamicLighting == null || !message.Params.HasValue)
            return IpcMessage.CreateResponse(message.Id!, false);

        var brightness = message.Params.Value.GetProperty("brightness").GetDouble();
        DynamicLighting.Brightness = brightness;
        return IpcMessage.CreateResponse(message.Id!, true);
    }

    private IpcMessage? HandleLightingCleanup(IpcMessage message)
    {
        DynamicLighting?.Cleanup();
        return IpcMessage.CreateResponse(message.Id!, true);
    }

    private IpcMessage? HandleLightingGetDevices(IpcMessage message)
    {
        if (DynamicLighting == null)
            return IpcMessage.CreateResponse(message.Id!, false);

        var devices = DynamicLighting.GetDevices();
        return IpcMessage.CreateResponse(message.Id!, true, new { devices });
    }
}
