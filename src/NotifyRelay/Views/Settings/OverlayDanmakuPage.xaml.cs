using NotifyRelay.ViewModels.Settings;

namespace NotifyRelay.Views.Settings;

/// <summary>覆盖层 - 弹幕通知子页。</summary>
public sealed partial class OverlayDanmakuPage : Page
{
    public DanmakuViewModel ViewModel => (DanmakuViewModel)DataContext;

    private string _currentTarget = "";

    public OverlayDanmakuPage()
    {
        InitializeComponent();
    }

    private void ColorPickerButton_Click(object sender, RoutedEventArgs e)
    {
        _currentTarget = "danmakuColor";
        ShowColorPicker(ViewModel.DanmakuColor);
    }

    private void BorderColorButton_Click(object sender, RoutedEventArgs e)
    {
        _currentTarget = "borderColor";
        ShowColorPicker(ViewModel.DanmakuBorderColor);
    }

    private void ShadowColorButton_Click(object sender, RoutedEventArgs e)
    {
        _currentTarget = "shadowColor";
        ShowColorPicker(ViewModel.DanmakuShadowColor);
    }

    private void ShowColorPicker(string hex)
    {
        ColorPicker.Color = ColorHex.ToWindowsColor(hex);
        _ = ColorPickerDialog.ShowAsync();
    }

    private void ColorPickerDialog_PrimaryButtonClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        var hex = ColorHex.Format(ColorPicker.Color);
        switch (_currentTarget)
        {
            case "danmakuColor":
                ViewModel.DanmakuColor = hex;
                break;
            case "borderColor":
                ViewModel.DanmakuBorderColor = hex;
                break;
            case "shadowColor":
                ViewModel.DanmakuShadowColor = hex;
                break;
        }
    }

    private void TestDanmakuButton_Click(object sender, RoutedEventArgs e)
    {
        ViewModel.SendTestDanmaku();
    }

    private void FontFamilyCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (sender is ComboBox cb && cb.SelectedItem is string font && ViewModel.DanmakuFontFamily != font)
        {
            ViewModel.DanmakuFontFamily = font;
        }
    }
}
