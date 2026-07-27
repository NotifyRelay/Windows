using NotifyRelay.Data.Contracts;
using NotifyRelay.Services;

namespace NotifyRelay.Platforms.Windows.Services;

public class WindowsActionService(
    IGeneralSettingsService generalSettingsService,
    ISessionManager sessionManager,
    IUserSettingsService userSettingsService,
    ILogger<WindowsActionService> logger) : BaseActionService(generalSettingsService, userSettingsService, sessionManager, logger)
{
}
