using NotifyRelay.Data.Contracts;
using Windows.Services.Store;
using WinRT.Interop;
using System.Runtime.InteropServices;

namespace NotifyRelay.Platforms.Windows.Services;
public partial class WindowsUpdateService : ObservableObject, IUpdateService
{
    private StoreContext? storeContext;
    private List<StorePackageUpdate>? updatePackages = [];

    private bool isUpdateAvailable;
    public bool IsUpdateAvailable
    {
        get => isUpdateAvailable;
        set => SetProperty(ref isUpdateAvailable, value);
    }

    public bool IsMandatory => updatePackages?.Where(e => e.Mandatory).ToList().Count >= 1;

    public async Task CheckForUpdatesAsync()
    {
        await GetUpdatePackagesAsync();

        if (updatePackages is not null && updatePackages.Count > 0)
        {
            isUpdateAvailable = true;
            return;
        }
        isUpdateAvailable = false;
    }

    public async Task DownloadUpdatesAsync()
    {
        var downloadOperation = storeContext?.RequestDownloadAndInstallStorePackageUpdatesAsync(updatePackages);
        await downloadOperation.AsTask();            
    }

    private async Task GetUpdatePackagesAsync()
    {
        try
        {
            storeContext ??= await Task.Run(StoreContext.GetDefault);

            InitializeWithWindow.Initialize(storeContext, App.WindowHandle);

            var updateList = await storeContext.GetAppAndOptionalStorePackageUpdatesAsync();
            updatePackages = updateList?.ToList();
        }
        catch (COMException comEx)
        {
            // 专门处理WinRT COM异常，记录详细日志
            System.Diagnostics.Debug.WriteLine($"Windows Update WinRT COM Exception: {comEx.Message}, HResult: {comEx.HResult}");
        }
        catch (Exception ex)
        {
            // 其他异常仍保持静默处理
            System.Diagnostics.Debug.WriteLine($"Windows Update Exception: {ex.Message}");
        }
    }
}
