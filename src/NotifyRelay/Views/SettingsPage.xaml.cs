namespace NotifyRelay.Views;

public sealed partial class SettingsPage : Page
{
    public SettingsPage()
    {
        InitializeComponent();
    }
    private void NavigationView_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        var selectedItem = (NavigationViewItem)args.SelectedItem;
        switch (selectedItem.Tag.ToString())
        {
            case "GeneralPage":
                SettingsContentFrame.Navigate(typeof(Settings.GeneralPage));
                break;
            case "ScrcpyAdbSettingsPage":
                SettingsContentFrame.Navigate(typeof(Settings.ScrcpyAdbSettingsPage));
                break;

            case "AboutPage":
                SettingsContentFrame.Navigate(typeof(Settings.AboutPage));
                break;

            case "DeviceSettingsPage":
                SettingsContentFrame.Navigate(typeof(DevicePreferences.DeviceSettingsPage));
                break;
        }
    }
}
