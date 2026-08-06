using NotifyRelay.Data.Contracts;
using NotifyRelay.Worker.Configuration;

namespace NotifyRelay.Services.Settings;

internal sealed class DeepSeekBalanceSettingsAccessor : IDeepSeekBalanceSettings
{
    private readonly IGeneralSettingsService _settings;

    public DeepSeekBalanceSettingsAccessor(IGeneralSettingsService settings)
    {
        _settings = settings;
    }

    public string? DeepSeekApiToken => _settings.DeepSeekApiToken;

    public int DeepSeekBalancePollingInterval => _settings.DeepSeekBalancePollingInterval;

    public string? DeepSeekBalanceHistoryJson
    {
        get => _settings.DeepSeekBalanceHistoryJson;
        set => _settings.DeepSeekBalanceHistoryJson = value;
    }
}
