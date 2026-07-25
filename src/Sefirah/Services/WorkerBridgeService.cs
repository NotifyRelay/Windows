using System.Diagnostics;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using NotifyRelay.Data.Contracts;
using NotifyRelay.Data.Models;

namespace NotifyRelay.Services;

public class WorkerBridgeService : IWorkerBridge, IDisposable
{
    private const string PipeName = "NotifyRelayWorker";
    private NamedPipeClientStream? _pipe;
    private CancellationTokenSource? _listenCts;
    private bool _disposed;
    private Process? _workerProcess;
    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public event EventHandler<BalanceHistoryItem>? DeepSeekBalanceUpdated;
    public event EventHandler? DeepSeekStatusChanged;
    public event EventHandler? DeepSeekHistoryChanged;
    public event EventHandler? BrightnessStatusChanged;
    public event EventHandler? LightingDevicesChanged;
    public event EventHandler<Windows.UI.Color>? LightingColorChanged;
    public event EventHandler<Windows.UI.Color>? LightingCapturedColorChanged;

    public bool IsConnected => _pipe?.IsConnected ?? false;

    public async Task<bool> ConnectAsync(TimeSpan timeout)
    {
        try
        {
            _pipe = new NamedPipeClientStream(".", PipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
            await _pipe.ConnectAsync((int)timeout.TotalMilliseconds);
            _listenCts = new CancellationTokenSource();
            _ = ListenAsync(_listenCts.Token);
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    public void Disconnect()
    {
        _listenCts?.Cancel();
        _pipe?.Dispose();
        _pipe = null;
    }

    public async Task<T?> SendCommandAsync<T>(string service, string method, object? parameters = null)
    {
        if (!IsConnected) return default;

        var id = Guid.NewGuid().ToString("N");
        var command = new
        {
            type = "command",
            id,
            service,
            method,
            @params = parameters
        };

        var json = JsonSerializer.Serialize(command, _jsonOptions);
        var bytes = Encoding.UTF8.GetBytes(json);

        await _pipe!.WriteAsync(bytes, 0, bytes.Length);
        await _pipe.FlushAsync();

        var responseBytes = new byte[4096];
        var readCount = await _pipe.ReadAsync(responseBytes, 0, responseBytes.Length);
        var responseJson = Encoding.UTF8.GetString(responseBytes, 0, readCount);

        try
        {
            var response = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(responseJson, _jsonOptions);
            if (response != null && response.TryGetValue("data", out var data))
            {
                return JsonSerializer.Deserialize<T>(data.GetRawText(), _jsonOptions);
            }
        }
        catch { }

        return default;
    }

    public async Task PushConfigAsync(Dictionary<string, object?> config)
    {
        if (!IsConnected) return;

        var msg = new { type = "configPush", config };
        var json = JsonSerializer.Serialize(msg, _jsonOptions);
        var bytes = Encoding.UTF8.GetBytes(json);

        await _pipe!.WriteAsync(bytes, 0, bytes.Length);
        await _pipe.FlushAsync();
    }

    public async Task StartWorkerProcessAsync()
    {
        var workerPath = Path.Combine(
            AppContext.BaseDirectory,
            "NotifyRelay.Worker.exe");

        if (!File.Exists(workerPath))
            workerPath = Path.Combine(
                AppContext.BaseDirectory,
                "..", "NotifyRelay.Worker",
                "bin", "Debug", "net9.0-windows10.0.26100",
                "NotifyRelay.Worker.exe");

        if (!File.Exists(workerPath))
        {
            // Try to find in solution directory
            var solutionDir = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "NotifyRelay.Worker"));
            workerPath = Path.Combine(solutionDir, "bin", "Debug", "net9.0-windows10.0.26100", "NotifyRelay.Worker.exe");
        }

        if (!File.Exists(workerPath))
        {
            return;
        }

        _workerProcess = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = workerPath,
                UseShellExecute = true,
                WindowStyle = ProcessWindowStyle.Hidden,
                CreateNoWindow = true
            },
            EnableRaisingEvents = true
        };

        _workerProcess.Exited += (s, e) =>
        {
            _workerProcess?.Dispose();
            _workerProcess = null;
        };

        _workerProcess.Start();
    }

    public async Task StopWorkerProcessAsync()
    {
        if (_workerProcess != null && !_workerProcess.HasExited)
        {
            try
            {
                var shutdown = new { type = "shutdown" };
                var json = JsonSerializer.Serialize(shutdown, _jsonOptions);
                var bytes = Encoding.UTF8.GetBytes(json);

                if (_pipe?.IsConnected == true)
                {
                    await _pipe.WriteAsync(bytes, 0, bytes.Length);
                    await _pipe.FlushAsync();
                }

                if (!_workerProcess.WaitForExit(5000))
                    _workerProcess.Kill();
            }
            catch { _workerProcess?.Kill(); }
        }

        Disconnect();
    }

    private async Task ListenAsync(CancellationToken token)
    {
        var buffer = new byte[4096];

        while (!token.IsCancellationRequested && _pipe?.IsConnected == true)
        {
            try
            {
                var readCount = await _pipe.ReadAsync(buffer, 0, buffer.Length, token);
                if (readCount == 0) break;

                var json = Encoding.UTF8.GetString(buffer, 0, readCount);
                var message = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json, _jsonOptions);

                if (message == null) continue;

                var type = message.GetValueOrDefault("type").GetString();
                if (type != "event") continue;

                var service = message.GetValueOrDefault("service").GetString();
                var eventName = message.GetValueOrDefault("event").GetString();

                DispatchEvent(service, eventName, message.GetValueOrDefault("data"));
            }
            catch (OperationCanceledException) { break; }
            catch (IOException) { break; }
            catch (Exception) { break; }
        }
    }

    private void DispatchEvent(string? service, string? eventName, JsonElement? data)
    {
        _ = App.MainWindow.DispatcherQueue.TryEnqueue(() =>
        {
            try
            {
                switch (service)
                {
                    case "deepseek":
                        HandleDeepSeekEvent(eventName, data);
                        break;
                    case "brightness":
                        HandleBrightnessEvent(eventName, data);
                        break;
                    case "lighting":
                        HandleLightingEvent(eventName, data);
                        break;
                }
            }
            catch { }
        });
    }

    private void HandleDeepSeekEvent(string? eventName, JsonElement? data)
    {
        switch (eventName)
        {
            case "balanceUpdated":
                if (data.HasValue)
                {
                    var item = JsonSerializer.Deserialize<BalanceHistoryItem>(data.Value.GetRawText(), _jsonOptions);
                    if (item != null)
                        DeepSeekBalanceUpdated?.Invoke(this, item);
                }
                break;
            case "statusChanged":
                DeepSeekStatusChanged?.Invoke(this, EventArgs.Empty);
                break;
        }
    }

    private void HandleBrightnessEvent(string? eventName, JsonElement? data)
    {
        switch (eventName)
        {
            case "statusChanged":
                BrightnessStatusChanged?.Invoke(this, EventArgs.Empty);
                break;
        }
    }

    private void HandleLightingEvent(string? eventName, JsonElement? data)
    {
        switch (eventName)
        {
            case "devicesChanged":
                LightingDevicesChanged?.Invoke(this, EventArgs.Empty);
                break;
            case "colorChanged":
            case "capturedColorChanged":
                if (data.HasValue &&
                    data.Value.TryGetProperty("r", out var r) &&
                    data.Value.TryGetProperty("g", out var g) &&
                    data.Value.TryGetProperty("b", out var b))
                {
                    var color = Windows.UI.Color.FromArgb(255, r.GetByte(), g.GetByte(), b.GetByte());
                    if (eventName == "colorChanged")
                        LightingColorChanged?.Invoke(this, color);
                    else
                        LightingCapturedColorChanged?.Invoke(this, color);
                }
                break;
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _listenCts?.Cancel();
        _pipe?.Dispose();
        _workerProcess?.Dispose();
    }
}
