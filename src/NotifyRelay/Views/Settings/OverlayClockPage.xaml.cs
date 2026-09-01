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
        var hex = ViewModel.ClockColor;
        if (!string.IsNullOrEmpty(hex) && hex.StartsWith("#") && hex.Length == 7)
        {
            byte r = byte.Parse(hex.Substring(1, 2), System.Globalization.NumberStyles.HexNumber);
            byte g = byte.Parse(hex.Substring(3, 2), System.Globalization.NumberStyles.HexNumber);
            byte b = byte.Parse(hex.Substring(5, 2), System.Globalization.NumberStyles.HexNumber);
            ColorPicker.Color = Windows.UI.Color.FromArgb(255, r, g, b);
        }
        _ = ColorPickerDialog.ShowAsync();
    }

    private void ColorPickerDialog_PrimaryButtonClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        var color = ColorPicker.Color;
        ViewModel.ClockColor = $"#{color.R:X2}{color.G:X2}{color.B:X2}";
    }
}
