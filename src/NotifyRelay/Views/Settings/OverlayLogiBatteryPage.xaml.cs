using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using NotifyRelay.ViewModels.Settings;

namespace NotifyRelay.Views.Settings;

public sealed partial class OverlayLogiBatteryPage : Page
{
    public LogiBatteryViewModel ViewModel { get; } = Ioc.Default.GetRequiredService<LogiBatteryViewModel>();

    public OverlayLogiBatteryPage()
    {
        this.InitializeComponent();

        // 手动设置 ComboBox 选中项（避免 TwoWay x:Bind 与 SelectionChanged 循环时 SelectedItem 不一致）
        // ScreenCombo 初始化后，根据 ViewModel.SelectedScreen 显式选中
        this.Loaded += (_, _) =>
        {
            if (ScreenCombo.SelectedItem == null && ViewModel.SelectedScreen != null)
                ScreenCombo.SelectedItem = ViewModel.SelectedScreen;
        };
    }

    private void RefreshButton_Click(object sender, RoutedEventArgs e)
    {
        ViewModel.ForceRefreshNow();
    }

    private void ScreenCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (e.AddedItems.Count > 0 && e.AddedItems[0] is ScreenOption opt)
        {
            // 仅在非相同值时推送：避免重复触发 PropertyChanged
            if (ViewModel.SelectedScreen != opt)
                ViewModel.SelectedScreen = opt;
        }
    }
}
