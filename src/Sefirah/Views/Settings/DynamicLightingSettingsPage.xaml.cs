namespace NotifyRelay.Views.Settings;

public sealed partial class DynamicLightingSettingsPage : Page
{
    private DynamicLightingViewModel ViewModel { get; }
    private bool _isColorDialogOpen;

    public DynamicLightingSettingsPage()
    {
        InitializeComponent();
        ViewModel = new DynamicLightingViewModel();
        DataContext = ViewModel;
        ViewModel.AutoRGBIntervalComboBox = AutoRGBIntervalComboBox;
    }

    private void ColorPickerButton_Click(object sender, RoutedEventArgs e)
    {
        if (_isColorDialogOpen)
            return;
        _isColorDialogOpen = true;

        var colorPicker = new ColorPicker
        {
            Color = ViewModel.CurrentColor,
            IsAlphaEnabled = false
        };

        var dialog = new ContentDialog
        {
            Title = "选择颜色",
            Content = colorPicker,
            PrimaryButtonText = "确定",
            CloseButtonText = "取消",
            XamlRoot = XamlRoot
        };

        dialog.PrimaryButtonClick += (s, args) =>
        {
            ViewModel.ApplyColor(colorPicker.Color);
        };

        dialog.Closed += (s, args) => _isColorDialogOpen = false;

        _ = dialog.ShowAsync();
    }

    private void TurnOffButton_Click(object sender, RoutedEventArgs e)
    {
        ViewModel.TurnOff();
    }

    private void StartEffectButton_Click(object sender, RoutedEventArgs e)
    {
        ViewModel.StartEffect(EffectComboBox.SelectedIndex);
    }

    private void StopEffectButton_Click(object sender, RoutedEventArgs e)
    {
        ViewModel.StopEffect();
    }
}