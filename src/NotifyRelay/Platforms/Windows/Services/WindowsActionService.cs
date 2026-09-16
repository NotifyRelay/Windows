using NotifyRelay.Data.Contracts;
// BaseActionService/DefaultActionsProvider 保留在 NotifyRelay.Services 根命名空间
using NotifyRelay.Services;
using NotifyRelay.Services.Devices;
using NotifyRelay.Services.Media;
using NotifyRelay.Services.Protocol;
using NotifyRelay.Services.Infrastructure;

namespace NotifyRelay.Platforms.Windows.Services;

public class WindowsActionService(
    IGeneralSettingsService generalSettingsService,
    IUserSettingsService userSettingsService,
    ILogger<WindowsActionService> logger) : BaseActionService(generalSettingsService, userSettingsService, logger)
{
}
