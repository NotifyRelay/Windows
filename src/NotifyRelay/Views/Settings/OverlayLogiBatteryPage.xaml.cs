using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using NotifyRelay.Models.Render;
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

    /// <summary>
    /// 用户编辑设备名失焦 → 写入 Provider 的 Override 字典（下次 FFI 刷新后仍保留）。
    /// 输入 null / 空白 / 与原始名相同时清除 Override（回退到 FFI）。
    /// </summary>
    private void DeviceNameTextBox_LostFocus(object sender, RoutedEventArgs e)
    {
        if (sender is not TextBox tb) return;
        if (tb.DataContext is not LogiBatteryDeviceInfo device) return;
        var provider = Ioc.Default.GetService<NotifyRelay.Services.LogiBatteryProvider>();
        if (provider == null) return;

        string? newName = tb.Text?.Trim();
        // 若用户清空，等价于清除 Override → 渲染会回落到 FFI 原始名
        provider.SetDeviceNameOverride(device.DeviceId, newName);
    }
}
