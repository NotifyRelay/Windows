using System.ComponentModel;
using NotifyRelay.Data.Contracts;
using NotifyRelay.Data.Models;

namespace NotifyRelay.ViewModels.Settings;

public partial class DevicesViewModel : ObservableObject
{
    #region Services
    private IDiscoveryService DiscoveryService { get; } = Ioc.Default.GetRequiredService<IDiscoveryService>();
    private IDeviceManager DeviceManager { get; } = Ioc.Default.GetRequiredService<IDeviceManager>();
    #endregion

    public ObservableCollection<PairedDevice> PairedDevices => DeviceManager.PairedDevices;

    public ObservableCollection<DiscoveredDevice> DiscoveredDevices => DiscoveryService.DiscoveredDevices;

    public DevicesViewModel()
    {
        // DiscoveryService 每轮刷新整体替换 DiscoveredDevices 实例（规避 ItemsRepeater 崩溃），
        // 此处转发通知，使 x:Bind OneWay 拿到新实例
        DiscoveryService.PropertyChanged += OnDiscoveryServicePropertyChanged;
    }

    private void OnDiscoveryServicePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(IDiscoveryService.DiscoveredDevices))
        {
            OnPropertyChanged(nameof(DiscoveredDevices));
        }
    }
}
