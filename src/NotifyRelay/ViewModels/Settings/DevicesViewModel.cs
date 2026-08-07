using Microsoft.UI.Dispatching;
using NotifyRelay.Data.Contracts;
using NotifyRelay.Data.Models;

namespace NotifyRelay.ViewModels.Settings;

public partial class DevicesViewModel : ObservableObject
{
    #region Services
    private readonly DispatcherQueue Dispatcher;
    private IDiscoveryService DiscoveryService { get; } = Ioc.Default.GetRequiredService<IDiscoveryService>();
    private IDeviceManager DeviceManager { get; } = Ioc.Default.GetRequiredService<IDeviceManager>();
    private IAdbService AdbService { get; } = Ioc.Default.GetRequiredService<IAdbService>();
    #endregion

    public ObservableCollection<PairedDevice> PairedDevices => DeviceManager.PairedDevices;
    public ObservableCollection<DiscoveredDevice> DiscoveredDevices => DiscoveryService.DiscoveredDevices;

    public DevicesViewModel()
    {
        Dispatcher = DispatcherQueue.GetForCurrentThread();
    }
}
