using Microsoft.UI.Xaml.Media.Animation;
using NotifyRelay.Data.Models;
using NotifyRelay.ViewModels.Settings;
using NotifyRelay.Views.DeviceSettings;

namespace NotifyRelay.Views.DevicePreferences;

public sealed partial class DeviceSettingsPage : Page
{
    public DeviceSettingsViewModel? ViewModel { get; set; }

    public DeviceSettingsPage()
    {
        InitializeComponent();
        
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        
        if (e.Parameter is PairedDevice device)
        {
            ViewModel = new DeviceSettingsViewModel(device);
        }
    }

    private void OpenNotificationsSettings(object sender, RoutedEventArgs e)
    {
        Frame.Navigate(typeof(NotificationSettingsPage), ViewModel, new DrillInNavigationTransitionInfo());
    }

    private void OpenClipboardSettings(object sender, RoutedEventArgs e)
    {
        Frame.Navigate(typeof(ClipboardSettingsPage), ViewModel, new DrillInNavigationTransitionInfo());
    }

    private void OpenScreenMirrorSettings(object sender, RoutedEventArgs e)
    {
        Frame.Navigate(typeof(ScreenMirrorSettingsPage), ViewModel, new DrillInNavigationTransitionInfo());
    }

    private void OpenAdbSettings(object sender, RoutedEventArgs e)
    {
        Frame.Navigate(typeof(AdbSettingsPage), ViewModel, new DrillInNavigationTransitionInfo());
    }
}
