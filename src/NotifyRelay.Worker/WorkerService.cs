using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NotifyRelay.Worker.Bridge;

namespace NotifyRelay.Worker.Services;

public class WorkerService : BackgroundService
{
    private readonly PipeServer _pipeServer;
    private readonly ServiceHost _serviceHost;
    private readonly ILogger<WorkerService> _logger;

    public WorkerService(PipeServer pipeServer, ServiceHost serviceHost, ILogger<WorkerService> logger)
    {
        _pipeServer = pipeServer;
        _serviceHost = serviceHost;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Worker service starting");

        _pipeServer.MessageHandler = async (message) =>
        {
            if (message.Type == "configPush")
            {
                if (message.Config != null)
                    await _serviceHost.PushConfigAsync(message.Config);
                return IpcMessage.CreateResponse("config", true);
            }
            return await _serviceHost.ExecuteCommandAsync(message);
        };

        _serviceHost.Initialize();

        await _pipeServer.StartAsync(stoppingToken);

        _logger.LogInformation("Worker service stopped");
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Worker service stopping");
        _serviceHost.Cleanup();
        await base.StopAsync(cancellationToken);
    }
}
