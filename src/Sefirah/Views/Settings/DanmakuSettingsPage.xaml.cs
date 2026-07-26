using NotifyRelay.Data.Items;
using NotifyRelay.ViewModels.Settings;

namespace NotifyRelay.Views.Settings;

public sealed partial class DanmakuSettingsPage : Page
{
    public DanmakuViewModel ViewModel => (DanmakuViewModel)DataContext;

    private string _currentTarget = "";

    public DanmakuSettingsPage()
    {
        InitializeComponent();
        SetupBreadcrumb();
    }

    private void SetupBreadcrumb()
    {
        BreadcrumbBar.ItemsSource = new ObservableCollection<BreadcrumbBarItemModel>
        {
            new("弹幕叠加层", typeof(DanmakuSettingsPage))
        };
        BreadcrumbBar.ItemClicked += BreadcrumbBar_ItemClicked;
    }

    private void BreadcrumbBar_ItemClicked(BreadcrumbBar sender, BreadcrumbBarItemClickedEventArgs args)
    {
        if (Frame.CanGoBack)
            Frame.GoBack();
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
}
