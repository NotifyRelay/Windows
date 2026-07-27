using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.UI;
using NotifyRelay.Data.Contracts;
using NotifyRelay.Worker.Services;

namespace NotifyRelay.Views.Settings;

public class DynamicLightingViewModel : INotifyPropertyChanged
{
    private readonly DynamicLightingService _lightingService;
    private readonly IGeneralSettingsService _settingsService;
    private readonly DispatcherQueue _dispatcher;

    private bool _isEnabled;
    private bool _isAutoRGBEnabled;
    private double _brightness = 1.0;
    private int _selectedEffectIndex;
    private int _selectedIntervalIndex;
    private Color _currentColor = new() { A = 255, R = 255, G = 255, B = 255 };
    private Color _currentCapturedColor = new() { A = 255, R = 0, G = 0, B = 0 };
    public event PropertyChangedEventHandler? PropertyChanged;

    public ObservableCollection<LampArrayDeviceDto> Devices { get; } = new();

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
                if (value)
                    _lightingService.Initialize();
                else
                    _lightingService.Cleanup();
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
                if (value)
                    _lightingService.StartAutoRGB();
                else
                    _lightingService.StopAutoRGB();
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
                OnPropertyChanged();
                OnPropertyChanged(nameof(BrightnessText));
                _lightingService.Brightness = value;
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
                ApplyEffect(value);
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
                if (AutoRGBIntervalComboBox?.Items.Count > value &&
                    AutoRGBIntervalComboBox.Items[value] is ComboBoxItem item &&
                    item.Tag is string tag)
                {
                    _settingsService.AutoRGBUpdateInterval = int.Parse(tag);
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
                _settingsService.DynamicLightingColor = value.ToString();
                _lightingService.SetColorFromString(value.ToString());
            }
        }
    }

    public SolidColorBrush CurrentColorBrush => new(_currentColor);

    public Color CurrentCapturedColor
    {
        get => _currentCapturedColor;
        set
        {
            if (_currentCapturedColor != value)
            {
                _currentCapturedColor = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(CurrentCapturedColorBrush));
            }
        }
    }

    public SolidColorBrush CurrentCapturedColorBrush => new(_currentCapturedColor);

    public string StatusText => _isEnabled ? "已启用" : "已禁用";
    public string DeviceCountText => $"{Devices.Count} 个设备已连接";
    public string DeviceListDescription => Devices.Count > 0 ? "以下设备支持动态光效" : "暂无设备";
    public string BrightnessText => $"当前亮度: {Math.Round(_brightness * 100)}%";
    public string AutoRGBStatusText => _isAutoRGBEnabled ? "运行中" : "已停止";
    public bool HasNoDevices => Devices.Count == 0;
    public string CaptureSupportText => "屏幕捕获可用";
    public bool IsCaptureSupported => true;

    public ComboBox AutoRGBIntervalComboBox { get; set; } = null!;

    public DynamicLightingViewModel()
    {
        _lightingService = Ioc.Default.GetRequiredService<DynamicLightingService>();
        _settingsService = Ioc.Default.GetRequiredService<IGeneralSettingsService>();
        _dispatcher = App.MainWindow.DispatcherQueue;

        LoadSettings();

        _lightingService.DevicesChanged += OnDevicesChanged;
        _lightingService.ColorChanged += OnColorChanged;
        _lightingService.CapturedColorChanged += OnCapturedColorChanged;
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
            50 => 0, 100 => 1, 200 => 2, 500 => 3, _ => 1
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
                        _currentColor = new Color { A = bytes[0], R = bytes[1], G = bytes[2], B = bytes[3] };
                    else if (bytes.Length == 3)
                        _currentColor = new Color { A = 255, R = bytes[0], G = bytes[1], B = bytes[2] };
                }
            }
            catch { }
        }
    }

    private void OnDevicesChanged()
    {
        FetchDevices();
    }

    private void OnColorChanged(Color e)
    {
        // ColorChanged 可能在后台线程（如屏幕捕获）触发，封送到 UI 线程以避免跨线程访问 XAML/WinRT 对象
        _dispatcher.TryEnqueue(() =>
        {
            CurrentColor = e;
            _settingsService.DynamicLightingColor = e.ToString();
        });
    }

    private void OnCapturedColorChanged(Color e)
    {
        // 捕获线程触发，必须封送到 UI 线程，否则绑定更新访问 Application.Resources 会抛 RPC_E_WRONG_THREAD
        _dispatcher.TryEnqueue(() => CurrentCapturedColor = e);
    }

    private void FetchDevices()
    {
        try
        {
            var devices = _lightingService.GetDevices();
            App.MainWindow.DispatcherQueue.TryEnqueue(() =>
            {
                Devices.Clear();
                foreach (var d in devices)
                    Devices.Add(d);
                OnPropertyChanged(nameof(DeviceCountText));
                OnPropertyChanged(nameof(DeviceListDescription));
                OnPropertyChanged(nameof(HasNoDevices));
            });
        }
        catch { }
    }

    private void ApplyEffect(int effectIndex)
    {
        switch (effectIndex)
        {
            case 0:
                _lightingService.StopAllEffects();
                _settingsService.DynamicLightingEffect = null;
                break;
            case 1:
                _lightingService.StartRainbowEffect();
                _settingsService.DynamicLightingEffect = "Rainbow";
                break;
            case 2:
                _lightingService.StartBlinkEffect(_currentColor);
                _settingsService.DynamicLightingEffect = "Blink";
                break;
        }
    }

    public void ApplyColor(Color color)
    {
        CurrentColor = color;
        _settingsService.DynamicLightingColor = color.ToString();
        _lightingService.SetColorFromString(color.ToString());
    }

    public void StartEffect(int effectIndex)
    {
        SelectedEffectIndex = effectIndex;
    }

    public void StopEffect()
    {
        SelectedEffectIndex = 0;
    }

    public void TurnOff()
    {
        if (_isAutoRGBEnabled) return;
        _lightingService.ApplyColorToDevices(new Color { A = 255, R = 0, G = 0, B = 0 });
        CurrentColor = new Color { A = 255, R = 0, G = 0, B = 0 };
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
