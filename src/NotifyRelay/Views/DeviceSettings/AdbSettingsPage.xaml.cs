using NotifyRelay.ViewModels.Settings;

namespace NotifyRelay.Views.DeviceSettings;

public sealed partial class AdbSettingsPage : Page
{
    public DeviceSettingsViewModel ViewModel
    {
        get => (DeviceSettingsViewModel)DataContext;
        private set => DataContext = value;
    }

    public AdbSettingsPage()
    {
        InitializeComponent();
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);

        if (e.Parameter is DeviceSettingsViewModel viewModel)
        {
            ViewModel = viewModel;
        }
    }

    private void BackButton_Click(object sender, RoutedEventArgs e)
    {
        if (Frame.CanGoBack)
        {
            Frame.GoBack();
        }
    }
}
