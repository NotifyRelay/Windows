using NotifyRelay.ViewModels.Settings;

namespace NotifyRelay.Views.Settings;

/// <summary>覆盖层 - DeepSeek 余额子页：监控开关、Token、查询间隔、显示设置与历史记录。</summary>
public sealed partial class OverlayDeepSeekBalancePage : Page
{
    public DeepSeekBalanceViewModel ViewModel { get; } = Ioc.Default.GetRequiredService<DeepSeekBalanceViewModel>();

    public OverlayDeepSeekBalancePage()
    {
        InitializeComponent();

        // 手动设置 ComboBox 选中项（避免 TwoWay x:Bind 与 SelectionChanged 循环时 SelectedItem 不一致）
        Loaded += (_, _) =>
        {
            if (ScreenCombo.SelectedItem == null && ViewModel.SelectedScreen != null)
                ScreenCombo.SelectedItem = ViewModel.SelectedScreen;
            UpdateStatusUI();
        };
    }

    private void UpdateStatusUI()
    {
        MonitorStatusTextBlock.Foreground = new SolidColorBrush(ViewModel.IsEnabled
            ? Microsoft.UI.Colors.Green
            : Microsoft.UI.Colors.Gray);
    }

    private void SaveToken_Click(object sender, RoutedEventArgs e) => ViewModel.SaveToken();

    private async void FetchNow_Click(object sender, RoutedEventArgs e)
    {
        if (await ViewModel.FetchBalanceAsync() != null) UpdateStatusUI();
    }

    private void ScreenCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (e.AddedItems.Count > 0 && e.AddedItems[0] is ScreenOption option && ViewModel.SelectedScreen != option)
            ViewModel.SelectedScreen = option;
    }

    private async void ClearHistory_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new ContentDialog
        {
            Title = "清除历史",
            Content = "确定要清除所有历史余额记录吗？此操作不可恢复。",
            PrimaryButtonText = "确定",
            CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = XamlRoot
        };

        if (await dialog.ShowAsync() == ContentDialogResult.Primary)
            ViewModel.ClearHistory();
    }
}
