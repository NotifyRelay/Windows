using NotifyRelay.Data.Contracts;
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
        BreadcrumbHelper.Setup(BreadcrumbBar, clickedPageType =>
        {
            if (clickedPageType != null && clickedPageType != typeof(DeviceDiscoveryPage))
            {
                // Navigate back to devices page
                if (Frame.CanGoBack)
                {
                    Frame.GoBack();
                }
            }
        },
            ("Devices.Title".GetLocalizedResource(), typeof(DeviceSettingsPage)),
            ("AvailableDevices/Title".GetLocalizedResource(), typeof(DeviceDiscoveryPage)));
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        DiscoveryService.StartDiscoveryAsync();
    }
}

