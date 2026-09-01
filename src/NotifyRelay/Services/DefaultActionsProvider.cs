using NotifyRelay.Data.Models.Actions;
using NotifyRelay.Platforms.Windows;

namespace NotifyRelay.Services;

public static class DefaultActionsProvider
{
    public static IEnumerable<BaseAction> GetDefaultActions()
    {
        return WindowsDefaultActions.GetDefaultActions();
    }
}
