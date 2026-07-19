using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.UI;
using NotifyRelay.DeviceCtrl.DynamicLighting;
using NotifyRelay.Data.Contracts;

namespace NotifyRelay.Views.Settings;

public class DynamicLightingViewModel : INotifyPropertyChanged
{
    private readonly DynamicLightingService _lightingService;
    private readonly IGeneralSettingsService _settingsService;
    private readonly ScreenColorAnalyzer _colorAnalyzer;

    private bool _isEnabled;
    private bool _isAutoRGBEnabled;
    private double _brightness;
    private int _selectedEffectIndex;
    private int _selectedIntervalIndex;
    private Color _currentColor = new() { A = 255, R = 255, G = 255, B = 255 };
    private bool _isCaptureSupported;

    public event PropertyChangedEventHandler? PropertyChanged;

    public ObservableCollection<LampArrayDeviceInfo> Devices => _lightingService.AttachedDevices;

    public bool IsEnabled
    {
        get => _isEnabled;
        set
        {
            if (_isEnabled != value)
            {
                _isEnabled = value;
                _settingsService.EnableDynamicLighting = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(StatusText));
            }
        }
    }

    public bool IsAutoRGBEnabled
    {
        get => _isAutoRGBEnabled;
        set
        {
            if (_isAutoRGBEnabled != value)
            {
                _isAutoRGBEnabled = value;
                _settingsService.EnableAutoRGB = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(AutoRGBStatusText));
                ToggleAutoRGB(value);
            }
        }
    }

    public double Brightness
    {
        get => _brightness;
        set
        {
            if (_brightness != value)
            {
                _brightness = value;
                _settingsService.DynamicLightingBrightness = value;
                _lightingService.Brightness = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(BrightnessText));
            }
        }
    }

    public int SelectedEffectIndex
    {
        get => _selectedEffectIndex;
        set
        {
            if (_selectedEffectIndex != value)
            {
                _selectedEffectIndex = value;
                OnPropertyChanged();
            }
        }
    }

    public int SelectedIntervalIndex
    {
        get => _selectedIntervalIndex;
        set
        {
            if (_selectedIntervalIndex != value)
            {
                _selectedIntervalIndex = value;
                if (AutoRGBIntervalComboBox != null && AutoRGBIntervalComboBox.Items.Count > value)
                {
                    _settingsService.AutoRGBUpdateInterval = int.Parse(((ComboBoxItem)AutoRGBIntervalComboBox.Items[value]).Tag.ToString()!);
                }
                OnPropertyChanged();
            }
        }
    }

    public Color CurrentColor
    {
        get => _currentColor;
        set
        {
            if (_currentColor != value)
            {
                _currentColor = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(CurrentColorBrush));
            }
        }
    }

    public SolidColorBrush CurrentColorBrush => new SolidColorBrush(_currentColor);

    public bool IsCaptureSupported
    {
        get => _isCaptureSupported;
        set
        {
            if (_isCaptureSupported != value)
            {
                _isCaptureSupported = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(CaptureSupportText));
            }
        }
    }

    public string StatusText => _isEnabled ? "已启用" : "已禁用";

    public string DeviceCountText => $"{Devices.Count} 个设备已连接";

    public string DeviceListDescription => Devices.Count > 0 ? "以下设备支持动态光效" : "暂无设备";

    public string BrightnessText => $"当前亮度: {Math.Round(_brightness * 100)}%";

    public string AutoRGBStatusText => _isAutoRGBEnabled ? "运行中" : "已停止";

    public string CaptureSupportText => IsCaptureSupported ? "屏幕捕获功能可用" : "屏幕捕获功能不可用";

    public bool HasNoDevices => Devices.Count == 0;

    public ComboBox AutoRGBIntervalComboBox { get; set; } = null!;

    public DynamicLightingViewModel()
    {
        _lightingService = Ioc.Default.GetRequiredService<DynamicLightingService>();
        _settingsService = Ioc.Default.GetRequiredService<IGeneralSettingsService>();
        _colorAnalyzer = new ScreenColorAnalyzer();

        LoadSettings();
        IsCaptureSupported = _colorAnalyzer.IsCaptureSupported;

        _lightingService.DevicesChanged += OnDevicesChanged;
        _lightingService.ColorChanged += OnColorChanged;
        _colorAnalyzer.ColorChanged += OnScreenColorChanged;
    }

    private void LoadSettings()
    {
        _isEnabled = _settingsService.EnableDynamicLighting;
        _isAutoRGBEnabled = _settingsService.EnableAutoRGB;
        _brightness = _settingsService.DynamicLightingBrightness;
        _selectedEffectIndex = 0;

        if (!string.IsNullOrEmpty(_settingsService.DynamicLightingEffect))
        {
            _selectedEffectIndex = _settingsService.DynamicLightingEffect switch
            {
                "Rainbow" => 1,
                "Blink" => 2,
                _ => 0
            };
        }

        var interval = _settingsService.AutoRGBUpdateInterval;
        _selectedIntervalIndex = interval switch
        {
            50 => 0,
            100 => 1,
            200 => 2,
            500 => 3,
            _ => 1
        };

        if (!string.IsNullOrEmpty(_settingsService.DynamicLightingColor))
            {
                try
                {
                    var colorString = _settingsService.DynamicLightingColor;
                    if (colorString.StartsWith("#"))
                    {
                        colorString = colorString.TrimStart('#');
                        var bytes = Convert.FromHexString(colorString);
                        if (bytes.Length == 4)
                        {
                            _currentColor = new Color { A = bytes[0], R = bytes[1], G = bytes[2], B = bytes[3] };
                        }
                        else if (bytes.Length == 3)
                        {
                            _currentColor = new Color { A = 255, R = bytes[0], G = bytes[1], B = bytes[2] };
                        }
                    }
                }
                catch
                {
                }
            }

        _lightingService.Brightness = _brightness;
        _lightingService.CurrentColor = _currentColor;
    }

    private void OnDevicesChanged(object? sender, EventArgs e)
    {
        OnPropertyChanged(nameof(DeviceCountText));
        OnPropertyChanged(nameof(DeviceListDescription));
        OnPropertyChanged(nameof(HasNoDevices));
    }

    private void OnColorChanged(object? sender, Color e)
    {
        CurrentColor = e;
        _settingsService.DynamicLightingColor = e.ToString();
    }

    private void OnScreenColorChanged(object? sender, Color e)
    {
        _lightingService.HandleAutoRGBColor(e);
    }

    private async void ToggleAutoRGB(bool enable)
    {
        if (enable)
        {
            if (IsCaptureSupported)
            {
                _lightingService.StartAutoRGB();
                await _colorAnalyzer.StartCaptureAsync();
            }
        }
        else
        {
            _lightingService.StopAutoRGB();
            _colorAnalyzer.StopCapture();
        }
    }

    public void ApplyColor(Color color)
    {
        _lightingService.CurrentColor = color;
        CurrentColor = color;
        _settingsService.DynamicLightingColor = color.ToString();
    }

    public void TurnOff()
    {
        _lightingService.TurnOffAllDevices();
        CurrentColor = new Color { A = 255, R = 0, G = 0, B = 0 };
    }

    public void StartEffect(int effectIndex)
    {
        switch (effectIndex)
        {
            case 1:
                _lightingService.StartRainbowEffect();
                _settingsService.DynamicLightingEffect = "Rainbow";
                break;
            case 2:
                _lightingService.StartBlinkEffect(CurrentColor);
                _settingsService.DynamicLightingEffect = "Blink";
                break;
        }
    }

    public void StopEffect()
    {
        _lightingService.StopAllEffects();
        _settingsService.DynamicLightingEffect = null;
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}