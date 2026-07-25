using System.IO.Pipes;
using System.Text;
using Microsoft.Extensions.Logging;

namespace NotifyRelay.Worker.Bridge;

public class PipeServer
{
    private const string PipeName = "NotifyRelayWorker";
    private readonly ServiceHost _serviceHost;
    private readonly ILogger<PipeServer> _logger;
    private NamedPipeServerStream? _currentPipe;

    public event EventHandler? ClientConnected;
    public event EventHandler? ClientDisconnected;

    public bool IsClientConnected => _currentPipe?.IsConnected ?? false;

    public PipeServer(ServiceHost serviceHost, ILogger<PipeServer> logger)
    {
        _serviceHost = serviceHost;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Pipe server starting on {PipeName}", PipeName);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var pipe = new NamedPipeServerStream(
                    PipeName, PipeDirection.InOut, 1,
                    PipeTransmissionMode.Message, PipeOptions.Asynchronous);

                _currentPipe = pipe;
                _logger.LogInformation("Waiting for client connection...");

                await pipe.WaitForConnectionAsync(stoppingToken);
                _logger.LogInformation("Client connected");

                ClientConnected?.Invoke(this, EventArgs.Empty);

                await HandleClientAsync(pipe, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Pipe server error");
                await Task.Delay(1000, stoppingToken);
            }
            finally
            {
                ClientDisconnected?.Invoke(this, EventArgs.Empty);
                _currentPipe = null;
            }
        }
    }

    private async Task HandleClientAsync(NamedPipeServerStream pipe, CancellationToken stoppingToken)
    {
        var buffer = new byte[4096];

        while (!stoppingToken.IsCancellationRequested && pipe.IsConnected)
        {
            try
            {
                var messageBuilder = new StringBuilder();
                int bytesRead;

                do
                {
                    bytesRead = await pipe.ReadAsync(buffer, 0, buffer.Length, stoppingToken);
                    messageBuilder.Append(Encoding.UTF8.GetString(buffer, 0, bytesRead));
                }
                while (!pipe.IsMessageComplete);

                var message = messageBuilder.ToString().TrimEnd('\0');
                if (string.IsNullOrEmpty(message))
                    continue;

                var response = await ProcessMessageAsync(message);

                if (response != null)
                {
                    var responseBytes = Encoding.UTF8.GetBytes(response.Serialize());
                    await pipe.WriteAsync(responseBytes, 0, responseBytes.Length, stoppingToken);
                    await pipe.FlushAsync(stoppingToken);
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (IOException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error handling pipe message");
            }
        }
    }

    private async Task<IpcMessage?> ProcessMessageAsync(string json)
    {
        var message = IpcMessage.Deserialize(json);
        if (message == null)
        {
            _logger.LogWarning("Failed to deserialize message: {Json}", json);
            return IpcMessage.CreateResponse("unknown", false);
        }

        switch (message.Type)
        {
            case "shutdown":
                _logger.LogInformation("Received shutdown command");
                Environment.Exit(0);
                return null;

            case "command":
                return await _serviceHost.ExecuteCommandAsync(message);

            case "configPush":
                if (message.Config != null)
                {
                    await _serviceHost.PushConfigAsync(message.Config);
                    return IpcMessage.CreateResponse("config", true);
                }
                return IpcMessage.CreateResponse("config", false);

            case "ping":
                return new IpcMessage { Type = "pong" };

            default:
                _logger.LogWarning("Unknown message type: {Type}", message.Type);
                return IpcMessage.CreateResponse(message.Id ?? "unknown", false);
        }
    }

    public async Task SendEventAsync(IpcMessage message)
    {
        if (_currentPipe?.IsConnected != true)
            return;

        try
        {
            var bytes = Encoding.UTF8.GetBytes(message.Serialize());
            await _currentPipe.WriteAsync(bytes, 0, bytes.Length);
            await _currentPipe.FlushAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send event");
        }
    }
}
