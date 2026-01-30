using NotifyRelay.Data.Contracts;
using NotifyRelay.Services;

namespace NotifyRelay.Platforms.Desktop.Services;

public class DesktopActionService(
    IGeneralSettingsService generalSettingsService,
    IUserSettingsService userSettingsService,
    ISessionManager sessionManager,
    ILogger<DesktopActionService> logger) : BaseActionService(generalSettingsService, userSettingsService, sessionManager, logger)
{
}
