using NotifyRelay.ViewModels.Settings;

namespace NotifyRelay.Views.DeviceSettings;

/// <summary>
/// DeviceSettings 四个子页的公共 code-behind：共享同一 <see cref="DeviceSettingsViewModel"/> 实例
/// 的接收契约与回退逻辑。
/// </summary>
public abstract class DeviceSettingsSubPageBase : Page
{
    public DeviceSettingsViewModel ViewModel
    {
        get => (DeviceSettingsViewModel)DataContext;
        private set => DataContext = value;
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);

        if (e.Parameter is DeviceSettingsViewModel viewModel)
        {
            ViewModel = viewModel;
        }
    }

    protected void BackButton_Click(object sender, RoutedEventArgs e)
    {
        if (Frame.CanGoBack)
        {
            Frame.GoBack();
        }
    }
}
