using NotifyRelay.Data.Contracts;

namespace NotifyRelay.Platforms.Desktop.Services;
public partial class DesktopUpdateService : ObservableObject, IUpdateService
{
    public bool IsUpdateAvailable => false;

    public Task CheckForUpdatesAsync()
    {
        return Task.CompletedTask;
    }

    public Task DownloadUpdatesAsync()
    {
        return Task.CompletedTask;
    }
}
