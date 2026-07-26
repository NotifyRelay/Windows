using NotifyRelay.Data.Contracts;
using NotifyRelay.Data.Models;
using NotifyRelay.Data.Models.Actions;

namespace NotifyRelay.Services;

public abstract class BaseActionService(
    IGeneralSettingsService generalSettingsService,
    IUserSettingsService userSettingsService,
    ISessionManager _sessionManager,
    ILogger logger) : IActionService
{
    public virtual Task InitializeAsync()
    {
        if (ApplicationData.Current.LocalSettings.Values["DefaultActionsLoaded"] == null)
        {
            ApplicationData.Current.LocalSettings.Values["DefaultActionsLoaded"] = true;
            var defaultActions = DefaultActionsProvider.GetDefaultActions();
            userSettingsService.GeneralSettingsService.Actions = [.. defaultActions];
        }

        return Task.CompletedTask;
    }

    public virtual void HandleActionMessage(ActionMessage action)
    {
        logger.LogInformation("正在执行动作：{name}", action.ActionName);
        var actionToExecute = generalSettingsService.Actions.FirstOrDefault(a => a.Id == action.ActionId);

        if (actionToExecute is not null && actionToExecute is ProcessAction processAction)
        {
            processAction.ExecuteAsync();
        }
    }
}
