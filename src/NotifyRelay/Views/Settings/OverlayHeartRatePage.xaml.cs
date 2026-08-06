using NotifyRelay.Services.HeartRate;
using NotifyRelay.ViewModels.Settings;

namespace NotifyRelay.Views.Settings;

/// <summary>覆盖层 - 心率子页：BLE 设备连接与浮动心率显示设置。</summary>
public sealed partial class OverlayHeartRatePage : Page
{
    public HeartRateViewModel ViewModel { get; }

    public OverlayHeartRatePage()
    {
        ViewModel = Ioc.Default.GetRequiredService<HeartRateViewModel>();
        InitializeComponent();
        // 恢复屏幕下拉选中项（VM 为单例，页面重建时同步 UI）
        ScreenCombo.SelectedItem = ViewModel.SelectedScreen;
    }

    private void ScanButton_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel.IsScanning)
            ViewModel.StopScan();
        else
            ViewModel.StartScan();
    }

    private async void ConnectButton_Click(object sender, RoutedEventArgs e)
    {
        await ViewModel.ConnectSelectedAsync();
    }

    private void DisconnectButton_Click(object sender, RoutedEventArgs e)
    {
        ViewModel.Disconnect();
    }

    private void DeviceListView_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        ViewModel.SelectedDevice = DeviceListView.SelectedItem as HeartRateDeviceInfo;
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
        var hex = ViewModel.HeartRateColor;
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
        ViewModel.HeartRateColor = $"#{color.R:X2}{color.G:X2}{color.B:X2}";
    }
}
