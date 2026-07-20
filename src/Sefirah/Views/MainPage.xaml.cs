using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media.Animation;
using NotifyRelay.ViewModels;
using NotifyRelay.ViewModels.Settings;
using Windows.ApplicationModel.DataTransfer;

namespace NotifyRelay.Views;

public sealed partial class MainPage : Page
{
    public MainPageViewModel ViewModel { get; }
    public DevicesViewModel DevicesViewModel { get; }

    public MainPage()
    {
        InitializeComponent();
        ViewModel = Ioc.Default.GetRequiredService<MainPageViewModel>();
        DevicesViewModel = Ioc.Default.GetRequiredService<DevicesViewModel>();
        DataContext = ViewModel;

        // 默认导航到应用列表页
        ContentFrame.Navigate(typeof(AppsPage));
    }

    private readonly Dictionary<string, Type> Pages = new()
    {
        { "Settings", typeof(SettingsPage) },
        { "Apps", typeof(AppsPage) },
        { "LocalNotificationHistory", typeof(LocalNotificationHistoryPage) },
        { "MonitorBrightness", typeof(Settings.MonitorBrightnessSettingsPage) },
        { "DeepSeekBalance", typeof(Settings.DeepSeekBalanceSettingsPage) },
        { "WallpaperOverlay", typeof(Settings.WallpaperOverlaySettingsPage) },
        { "VirtualSpeaker", typeof(Settings.VirtualSpeakerSettingsPage) }
    };

    // Track the current animation to prevent conflicts
    private Storyboard? currentOverlayAnimation;

    // Handle mouse wheel events on the phone frame
    private void PhoneFrame_PointerWheelChanged(object sender, PointerRoutedEventArgs e)
    {
        // Get the wheel delta - positive for scrolling up, negative for scrolling down
        var pointerPoint = e.GetCurrentPoint(PhoneFrameGrid);
        int wheelDelta = pointerPoint.Properties.MouseWheelDelta;
        ViewModel.SwitchToNextDevice(wheelDelta);
        e.Handled = true;
    }

    private void NavigationView_SelectionChanged(NavigationView _, NavigationViewSelectionChangedEventArgs args)
    {
        if (args.SelectedItem is NavigationViewItem selectedItem &&
            selectedItem.Tag?.ToString() is string tag &&
            Pages.TryGetValue(tag, out Type? pageType))
        {
            ContentFrame.Navigate(pageType);
        }
    }

    private void PhoneFrame_PointerEntered(object sender, PointerRoutedEventArgs e)
    {
        AnimateOverlay(PhoneFrameOverlay, true);
    }

    private void PhoneFrame_PointerExited(object sender, PointerRoutedEventArgs e)
    {
        AnimateOverlay(PhoneFrameOverlay, false);
    }

    private void AnimateOverlay(UIElement overlay, bool show)
    {
        // Cancel any existing animation to prevent conflicts
        currentOverlayAnimation?.Stop();
        currentOverlayAnimation = null;

        if (show)
        {
            overlay.Visibility = Visibility.Visible;
            currentOverlayAnimation = FadeInStoryboard;
            FadeInStoryboard.Begin();
        }
        else
        {
            currentOverlayAnimation = FadeOutStoryboard;
            FadeOutStoryboard.Begin();

            // Hide overlay after animation completes
            FadeOutStoryboard.Completed += (s, args) =>
            {
                overlay.Visibility = Visibility.Collapsed;
                currentOverlayAnimation = null;
            };
        }
    }

    private async void Page_Drop(object sender, DragEventArgs e)
    {
        // Check if the dropped data contains files
        if (e.DataView.Contains(StandardDataFormats.StorageItems))
        {
            ViewModel.SendFiles(await e.DataView.GetStorageItemsAsync());
        }
    }

    private void Grid_DragOver(object sender, DragEventArgs e)
    {
        if (ViewModel.PairedDevices.Count == 0) return;

        e.AcceptedOperation = DataPackageOperation.Copy;
        if (ViewModel.PairedDevices.Count == 1)
        {
            e.DragUIOverride.Caption = $"Send to {ViewModel.Device?.Name}";
        }
    }
}