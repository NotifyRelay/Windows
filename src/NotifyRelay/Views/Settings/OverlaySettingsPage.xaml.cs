using NotifyRelay.Data.Items;

namespace NotifyRelay.Views.Settings;

/// <summary>覆盖层设置容器页：左侧导航切换 弹幕通知 / Top 卡片 / 心率 三个子页。</summary>
public sealed partial class OverlaySettingsPage : Page
{
    public OverlaySettingsPage()
    {
        InitializeComponent();
        SetupBreadcrumb();
    }

    private void SetupBreadcrumb()
    {
        BreadcrumbBar.ItemsSource = new ObservableCollection<BreadcrumbBarItemModel>
        {
            new("覆盖层", typeof(OverlaySettingsPage))
        };
        BreadcrumbBar.ItemClicked += BreadcrumbBar_ItemClicked;
    }

    private void BreadcrumbBar_ItemClicked(BreadcrumbBar sender, BreadcrumbBarItemClickedEventArgs args)
    {
        if (Frame.CanGoBack)
            Frame.GoBack();
    }

    private void NavigationView_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        var selectedItem = (NavigationViewItem)args.SelectedItem;
        switch (selectedItem.Tag.ToString())
        {
            case "DanmakuPage":
                OverlayContentFrame.Navigate(typeof(OverlayDanmakuPage));
                break;
            case "TopCardsPage":
                OverlayContentFrame.Navigate(typeof(OverlayTopCardsPage));
                break;
            case "HeartRatePage":
                OverlayContentFrame.Navigate(typeof(OverlayHeartRatePage));
                break;
            case "KeyboardPage":
                OverlayContentFrame.Navigate(typeof(OverlayKeyboardPage));
                break;
            case "LogiBatteryPage":
                OverlayContentFrame.Navigate(typeof(OverlayLogiBatteryPage));
                break;
            case "ClockPage":
                OverlayContentFrame.Navigate(typeof(OverlayClockPage));
                break;
        }
    }
}
