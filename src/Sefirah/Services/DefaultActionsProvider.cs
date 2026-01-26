using NotifyRelay.Data.Models.Actions;
#if WINDOWS
using NotifyRelay.Platforms.Windows;
#elif DESKTOP
using NotifyRelay.Platforms.Desktop;
#endif

namespace NotifyRelay.Services;

public static class DefaultActionsProvider
{
    public static IEnumerable<BaseAction> GetDefaultActions()
    {
#if WINDOWS
        return WindowsDefaultActions.GetDefaultActions();
#elif DESKTOP
        return DesktopDefaultActions.GetDefaultActions();
#endif
    }
} 
