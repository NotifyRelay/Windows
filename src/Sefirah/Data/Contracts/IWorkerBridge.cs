using NotifyRelay.Data.Models;

namespace NotifyRelay.Data.Contracts;

public interface IWorkerBridge
{
    event EventHandler<BalanceHistoryItem>? DeepSeekBalanceUpdated;
    event EventHandler? DeepSeekStatusChanged;
    event EventHandler? DeepSeekHistoryChanged;

    event EventHandler? BrightnessStatusChanged;

    event EventHandler? LightingDevicesChanged;
    event EventHandler<Windows.UI.Color>? LightingColorChanged;
    event EventHandler<Windows.UI.Color>? LightingCapturedColorChanged;

    bool IsConnected { get; }
    Task<bool> ConnectAsync(TimeSpan timeout);
    void Disconnect();

    Task<T?> SendCommandAsync<T>(string service, string method, object? parameters = null);

    Task PushConfigAsync(Dictionary<string, object?> config);

    Task StartWorkerProcessAsync();
    Task StopWorkerProcessAsync();
}
