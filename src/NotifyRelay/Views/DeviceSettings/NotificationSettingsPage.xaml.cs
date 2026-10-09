using NotifyRelay.ViewModels.Settings;

namespace NotifyRelay.Views.DeviceSettings;

public sealed partial class NotificationSettingsPage : DeviceSettingsSubPageBase
{
    public NotificationSettingsPage()
    {
        InitializeComponent();
    }

    public void OnMenuFlyoutItemClick(object sender, RoutedEventArgs e)
    {
        if (sender is MenuFlyoutItem menuItem &&
            menuItem.Tag is string appPackage)
        {
            ViewModel.ChangeNotificationFilter(menuItem.Text, appPackage);
        }
    }
}
