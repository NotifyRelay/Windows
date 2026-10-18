using NotifyRelay.ViewModels.Settings;

namespace NotifyRelay.Views.Settings;

/// <summary>覆盖层 - 时间浮窗子页：显示开关、样式与位置设置。</summary>
public sealed partial class OverlayClockPage : Page
{
    public ClockViewModel ViewModel { get; }

    public OverlayClockPage()
    {
        ViewModel = Ioc.Default.GetRequiredService<ClockViewModel>();
        InitializeComponent();
        // 恢复屏幕下拉选中项（VM 为单例，页面重建时同步 UI）
        ScreenCombo.SelectedItem = ViewModel.SelectedScreen;
    }

    private void ScreenCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ScreenCombo.SelectedItem is ScreenOption option && ViewModel.SelectedScreen != option)
        {
            ViewModel.SelectedScreen = option;
        }
    }

    private void ColorPickerButton_Click(object sender, RoutedEventArgs e)
    {
        ColorPicker.Color = ColorHex.ToWindowsColor(ViewModel.ClockColor);
        _ = ColorPickerDialog.ShowAsync();
    }

    private void ColorPickerDialog_PrimaryButtonClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        ViewModel.ClockColor = ColorHex.Format(ColorPicker.Color);
    }
}
