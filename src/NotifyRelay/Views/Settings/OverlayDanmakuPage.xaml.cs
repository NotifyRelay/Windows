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
        var hex = $"#{color.R:X2}{color.G:X2}{color.B:X2}";
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
