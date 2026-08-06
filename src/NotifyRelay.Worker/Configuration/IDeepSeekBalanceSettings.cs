namespace NotifyRelay.Worker.Configuration;

/// <summary>
/// Provides read/write access to DeepSeek balance monitoring settings.
/// Implemented by the host application to persist changes back to the settings store.
/// </summary>
public interface IDeepSeekBalanceSettings
{
    string? DeepSeekApiToken { get; }

    int DeepSeekBalancePollingInterval { get; }

    string? DeepSeekBalanceHistoryJson { get; set; }
}
