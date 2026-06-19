using Microsoft.UI.Xaml.Data;
using NotifyRelay.Data.Items;
using NotifyRelay.Helpers;
using NotifyRelay.ViewModels.Settings;

namespace NotifyRelay.Views.Settings;

public sealed partial class WallpaperOverlaySettingsPage : Page
{
    public WallpaperOverlayViewModel ViewModel => (WallpaperOverlayViewModel)DataContext;

    public WallpaperOverlaySettingsPage()
    {
        InitializeComponent();
        SetupBreadcrumb();
        InitializeEditor();
        ViewModel.PropertyChanged += ViewModel_PropertyChanged;
    }

    private void ViewModel_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(WallpaperOverlayViewModel.TextAlignment))
        {
            UpdatePreviewAlignment();
        }
    }

    private void SetupBreadcrumb()
    {
        BreadcrumbBar.ItemsSource = new ObservableCollection<BreadcrumbBarItemModel>
        {
            new("壁纸层显示", typeof(WallpaperOverlaySettingsPage))
        };
        BreadcrumbBar.ItemClicked += BreadcrumbBar_ItemClicked;
    }

    private void InitializeEditor()
    {
        MarkdownEditor.Text = ViewModel.DisplayText;
        UpdateMarkdownPreview();
        UpdatePreviewAlignment();
    }

    private void UpdatePreviewAlignment()
    {
        var align = ViewModel.TextAlignment;
        MarkdownPreview.HorizontalAlignment = align switch
        {
            "左对齐" => Microsoft.UI.Xaml.HorizontalAlignment.Left,
            "右对齐" => Microsoft.UI.Xaml.HorizontalAlignment.Right,
            _ => Microsoft.UI.Xaml.HorizontalAlignment.Center
        };
        MarkdownPreview.TextAlignment = align switch
        {
            "左对齐" => Microsoft.UI.Xaml.TextAlignment.Left,
            "右对齐" => Microsoft.UI.Xaml.TextAlignment.Right,
            _ => Microsoft.UI.Xaml.TextAlignment.Center
        };
    }

    private void MarkdownEditor_TextChanged(object sender, TextChangedEventArgs e)
    {
        ViewModel.DisplayText = MarkdownEditor.Text;
        UpdateMarkdownPreview();
    }

    private void UpdateMarkdownPreview()
    {
        var foreground = ColorHelper.CreateBrush(ViewModel.TextColor);
        MarkdownRenderer.RenderToInlines(MarkdownEditor.Text, MarkdownPreview.Inlines, foreground, 16);
    }

    private void BreadcrumbBar_ItemClicked(BreadcrumbBar sender, BreadcrumbBarItemClickedEventArgs args)
    {
        if (Frame.CanGoBack)
        {
            Frame.GoBack();
        }
    }

    private void PreviewButton_Click(object sender, RoutedEventArgs e)
    {
        ViewModel.ShowOverlay();
    }

    private void HideButton_Click(object sender, RoutedEventArgs e)
    {
        ViewModel.HideOverlay();
    }

    private void ColorPickerButton_Click(object sender, RoutedEventArgs e)
    {
        var currentColor = ViewModel.TextColor;
        if (!string.IsNullOrEmpty(currentColor) && currentColor.StartsWith("#"))
        {
            string hex = currentColor.TrimStart('#');
            if (hex.Length == 6)
            {
                byte r = byte.Parse(hex.Substring(0, 2), System.Globalization.NumberStyles.HexNumber);
                byte g = byte.Parse(hex.Substring(2, 2), System.Globalization.NumberStyles.HexNumber);
                byte b = byte.Parse(hex.Substring(4, 2), System.Globalization.NumberStyles.HexNumber);
                ColorPicker.Color = new Windows.UI.Color { R = r, G = g, B = b, A = 255 };
            }
        }
        ColorPickerDialog.ShowAsync();
    }

    private void ColorPickerDialog_PrimaryButtonClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        var color = ColorPicker.Color;
        ViewModel.TextColor = $"#{color.R:X2}{color.G:X2}{color.B:X2}";
        UpdateMarkdownPreview();
    }
}

public class BoolToStatusConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        if (value is bool isEnabled)
        {
            return isEnabled ? "状态：已启用" : "状态：未启用";
        }
        return "状态：未启用";
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        throw new NotImplementedException();
    }
}

public class IntToStringConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        if (value is int intValue)
        {
            return intValue.ToString();
        }
        return string.Empty;
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        if (value is string strValue && int.TryParse(strValue, out int result))
        {
            return result;
        }
        return 24;
    }
}
