using Microsoft.UI.Xaml.Media.Animation;
using NotifyRelay.Data.Contracts;
using NotifyRelay.ViewModels.Settings;

namespace NotifyRelay.Views.Onboarding;

public sealed partial class SyncPage : Page
{
    private IDiscoveryService DiscoveryService { get; } = Ioc.Default.GetRequiredService<IDiscoveryService>();
    public DevicesViewModel ViewModel { get; }

    public SyncPage()
    {
        InitializeComponent();
        ViewModel = Ioc.Default.GetRequiredService<DevicesViewModel>();
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        DiscoveryService.StartDiscoveryAsync();
    }

    private void SkipButton_Click(object sender, RoutedEventArgs e)
    {
        // Mark onboarding as completed
        ApplicationData.Current.LocalSettings.Values["HasCompletedOnboarding"] = true;
        Frame.Navigate(typeof(MainPage), null, new DrillInNavigationTransitionInfo());
    }

    protected override void OnNavigatingFrom(NavigatingCancelEventArgs e)
    {
        base.OnNavigatingFrom(e);
    }
}
