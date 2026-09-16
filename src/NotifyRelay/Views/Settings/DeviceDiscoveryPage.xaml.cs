using NotifyRelay.Data.Contracts;
using NotifyRelay.Data.Items;
using NotifyRelay.ViewModels.Settings;
using NotifyRelay.Views.DevicePreferences;

namespace NotifyRelay.Views.Settings;

public sealed partial class DeviceDiscoveryPage : Page
{
    private IDiscoveryService DiscoveryService { get; } = Ioc.Default.GetRequiredService<IDiscoveryService>();

    /// <summary>
    /// 使用 DI 单例 ViewModel（而非 XAML 内 <c>new</c>）：
    /// <see cref="DevicesViewModel"/> 订阅了单例 <see cref="IDiscoveryService"/> 的 PropertyChanged，
    /// 每进入本页新建实例会累积订阅并让旧实例持续收到通知。
    /// </summary>
    public DevicesViewModel ViewModel { get; } = Ioc.Default.GetRequiredService<DevicesViewModel>();

    public DeviceDiscoveryPage()
    {
        InitializeComponent();
        SetupBreadcrumb();
    }

    private void SetupBreadcrumb()
    {
        BreadcrumbBar.ItemsSource = new ObservableCollection<BreadcrumbBarItemModel>
        {
            new("Devices.Title".GetLocalizedResource(), typeof(DeviceSettingsPage)),
            new("AvailableDevices/Title".GetLocalizedResource(), typeof(DeviceDiscoveryPage))
        };
        BreadcrumbBar.ItemClicked += BreadcrumbBar_ItemClicked;
    }

    private void BreadcrumbBar_ItemClicked(BreadcrumbBar sender, BreadcrumbBarItemClickedEventArgs args)
    {
        var items = BreadcrumbBar.ItemsSource as ObservableCollection<BreadcrumbBarItemModel>;
        var clickedItem = items?[args.Index];

        if (clickedItem?.PageType != null && clickedItem.PageType != typeof(DeviceDiscoveryPage))
        {
            // Navigate back to devices page
            if (Frame.CanGoBack)
            {
                Frame.GoBack();
            }
        }
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        DiscoveryService.StartDiscoveryAsync();
    }

    protected override void OnNavigatingFrom(NavigatingCancelEventArgs e)
    {
        base.OnNavigatingFrom(e);
    }
}

