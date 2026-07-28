using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Xaml.Navigation;
using NotifyRelay.Data.Contracts;
using NotifyRelay.Data.Models;
using NotifyRelay.ViewModels.Settings;
using NotifyRelay.Views.DeviceSettings;
using NotifyRelay.Views.Settings;

namespace NotifyRelay.Views.DevicePreferences;

public sealed partial class DeviceSettingsPage : Page
{
    private readonly IDeviceManager DeviceManager = Ioc.Default.GetRequiredService<IDeviceManager>();

    public DeviceSettingsViewModel? ViewModel { get; set; }

    public DeviceSettingsPage()
    {
        InitializeComponent();

        if (DeviceManager is INotifyPropertyChanged npc)
        {
            npc.PropertyChanged += DeviceManager_PropertyChanged;
            Unloaded += (_, _) => npc.PropertyChanged -= DeviceManager_PropertyChanged;
        }
    }

    private void DeviceManager_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(IDeviceManager.ActiveDevice))
        {
            LoadActiveDeviceSettings();
        }
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        LoadActiveDeviceSettings();
    }

    private void LoadActiveDeviceSettings()
    {
        var device = DeviceManager.ActiveDevice;
        if (device != null)
        {
            ViewModel = new DeviceSettingsViewModel(device);
            SettingsContent.Visibility = Visibility.Visible;
            EmptyState.Visibility = Visibility.Collapsed;
            Bindings.Update();
        }
        else
        {
            ViewModel = null;
            SettingsContent.Visibility = Visibility.Collapsed;
            EmptyState.Visibility = Visibility.Visible;
        }
    }

    private async void DeleteDevice_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel?.Device is { } device)
        {
            await ViewModel.RemoveDevice(device);
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

    private void OpenAvailableDevices(object sender, RoutedEventArgs e)
    {
        Frame.Navigate(typeof(DeviceDiscoveryPage), null, new DrillInNavigationTransitionInfo());
    }
}
