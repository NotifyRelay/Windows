using Windows.Devices.Enumeration;
using Windows.Devices.Lights;
using Windows.Devices.Lights.Effects;
using Windows.UI;
using NotifyRelay.DeviceCtrl.DynamicLighting.Interfaces;

namespace NotifyRelay.DeviceCtrl.DynamicLighting;

public class DynamicLightingService
{
    private readonly ILogger<DynamicLightingService> _logger;
    private DeviceWatcher? _deviceWatcher;
    private readonly ObservableCollection<LampArrayDeviceInfo> _attachedDevices = new();
    private IReadOnlyList<LampArrayEffectPlaylist> _currentPlaylists = Array.Empty<LampArrayEffectPlaylist>();
    private bool _isAutoRGBEnabled;
    private double _brightness = 1.0;
    private Color _currentColor = new() { A = 255, R = 255, G = 255, B = 255 };
    private readonly List<ILightingInputProvider> _inputProviders = new();

    public event EventHandler? DevicesChanged;
    public event EventHandler<Color>? ColorChanged;

    public ObservableCollection<LampArrayDeviceInfo> AttachedDevices => _attachedDevices;

    public bool IsAutoRGBEnabled => _isAutoRGBEnabled;

    public double Brightness
    {
        get => _brightness;
        set
        {
            _brightness = Math.Clamp(value, 0, 1);
            UpdateBrightnessOnDevices();
        }
    }

    public Color CurrentColor
    {
        get => _currentColor;
        set
        {
            _currentColor = value;
            ApplyColorToDevices(value);
            ColorChanged?.Invoke(this, value);
        }
    }

    public DynamicLightingService(ILogger<DynamicLightingService> logger)
    {
        _logger = logger;
    }

    public void Initialize()
    {
        try
        {
            _deviceWatcher = DeviceInformation.CreateWatcher(LampArray.GetDeviceSelector());
            _deviceWatcher.Added += DeviceWatcher_Added;
            _deviceWatcher.Removed += DeviceWatcher_Removed;
            _deviceWatcher.Start();
            _logger.LogInformation("Dynamic lighting service initialized");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to initialize dynamic lighting service");
        }
    }

    public void Cleanup()
    {
        StopAutoRGB();
        StopAllEffects();
        
        if (_deviceWatcher != null)
        {
            _deviceWatcher.Stop();
            _deviceWatcher.Added -= DeviceWatcher_Added;
            _deviceWatcher.Removed -= DeviceWatcher_Removed;
            _deviceWatcher = null;
        }

        foreach (var provider in _inputProviders)
        {
            provider.Stop();
        }
        _inputProviders.Clear();

        _attachedDevices.Clear();

        _logger.LogInformation("Dynamic lighting service cleaned up");
    }

    private async void DeviceWatcher_Added(DeviceWatcher sender, DeviceInformation args)
    {
        try
        {
            var lampArray = await LampArray.FromIdAsync(args.Id);
            if (lampArray == null)
            {
                _logger.LogWarning($"Failed to initialize LampArray device: {args.Name}");
                return;
            }

            var deviceInfo = new LampArrayDeviceInfo
            {
                Id = args.Id,
                Name = args.Name,
                LampArray = lampArray,
                IsAvailable = lampArray.IsAvailable,
                LampCount = lampArray.LampCount,
                Kind = lampArray.LampArrayKind
            };

            lampArray.AvailabilityChanged += LampArray_AvailabilityChanged;

            App.MainWindow.DispatcherQueue.TryEnqueue(() =>
            {
                _attachedDevices.Add(deviceInfo);
                deviceInfo.LampArray!.BrightnessLevel = _brightness;
                deviceInfo.LampArray.SetColor(_currentColor);
                DevicesChanged?.Invoke(this, EventArgs.Empty);
            });

            _logger.LogInformation($"LampArray device added: {args.Name} ({args.Id})");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"Error adding LampArray device: {args.Name}");
        }
    }

    private void DeviceWatcher_Removed(DeviceWatcher sender, DeviceInformationUpdate args)
    {
        App.MainWindow.DispatcherQueue.TryEnqueue(() =>
        {
            var device = _attachedDevices.FirstOrDefault(d => d.Id == args.Id);
            if (device != null)
            {
                _attachedDevices.Remove(device);
                DevicesChanged?.Invoke(this, EventArgs.Empty);
                _logger.LogInformation($"LampArray device removed: {device.Name}");
            }
        });
    }

    private void LampArray_AvailabilityChanged(LampArray sender, object args)
    {
        App.MainWindow.DispatcherQueue.TryEnqueue(() =>
        {
            var device = _attachedDevices.FirstOrDefault(d => d.LampArray == sender);
            if (device != null)
            {
                device.IsAvailable = sender.IsAvailable;
                DevicesChanged?.Invoke(this, EventArgs.Empty);
            }
        });
    }

    public void ApplyColorToDevices(Color color)
    {
        foreach (var device in _attachedDevices)
        {
            if (device.IsAvailable && device.LampArray != null)
            {
                try
                {
                    device.LampArray.SetColor(color);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, $"Failed to apply color to device {device.Name}");
                }
            }
        }
    }

    public void TurnOffAllDevices()
    {
        ApplyColorToDevices(new Color { A = 255, R = 0, G = 0, B = 0 });
    }

    public void UpdateBrightnessOnDevices()
    {
        foreach (var device in _attachedDevices)
        {
            if (device.IsAvailable && device.LampArray != null)
            {
                try
                {
                    device.LampArray.BrightnessLevel = _brightness;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, $"Failed to update brightness on device {device.Name}");
                }
            }
        }
    }

    public void StartRainbowEffect()
    {
        StopAllEffects();

        var playlists = new List<LampArrayEffectPlaylist>();

        foreach (var device in _attachedDevices)
        {
            if (!device.IsAvailable || device.LampArray == null)
                continue;

            var playlist = new LampArrayEffectPlaylist
            {
                EffectStartMode = LampArrayEffectStartMode.Sequential,
                RepetitionMode = LampArrayRepetitionMode.Forever
            };

            int[] indices = Enumerable.Range(0, device.LampCount).ToArray();

            playlist.Append(new LampArrayColorRampEffect(device.LampArray, indices)
            {
                Color = new Color { A = 255, R = 255, G = 0, B = 0 },
                RampDuration = TimeSpan.FromMilliseconds(500),
                CompletionBehavior = LampArrayEffectCompletionBehavior.KeepState
            });

            playlist.Append(new LampArrayColorRampEffect(device.LampArray, indices)
            {
                Color = new Color { A = 255, R = 255, G = 255, B = 0 },
                RampDuration = TimeSpan.FromMilliseconds(500),
                CompletionBehavior = LampArrayEffectCompletionBehavior.KeepState
            });

            playlist.Append(new LampArrayColorRampEffect(device.LampArray, indices)
            {
                Color = new Color { A = 255, R = 0, G = 255, B = 0 },
                RampDuration = TimeSpan.FromMilliseconds(500),
                CompletionBehavior = LampArrayEffectCompletionBehavior.KeepState
            });

            playlist.Append(new LampArrayColorRampEffect(device.LampArray, indices)
            {
                Color = new Color { A = 255, R = 0, G = 0, B = 255 },
                RampDuration = TimeSpan.FromMilliseconds(500),
                CompletionBehavior = LampArrayEffectCompletionBehavior.KeepState
            });

            playlists.Add(playlist);
        }

        LampArrayEffectPlaylist.StartAll(playlists);
        _currentPlaylists = playlists;
        _logger.LogInformation("Rainbow effect started");
    }

    public void StartBlinkEffect(Color color)
    {
        StopAllEffects();

        var playlists = new List<LampArrayEffectPlaylist>();

        foreach (var device in _attachedDevices)
        {
            if (!device.IsAvailable || device.LampArray == null)
                continue;

            var playlist = new LampArrayEffectPlaylist();

            int[] indices = Enumerable.Range(0, device.LampCount).ToArray();

            playlist.Append(new LampArrayBlinkEffect(device.LampArray, indices)
            {
                Color = color,
                AttackDuration = TimeSpan.FromMilliseconds(300),
                SustainDuration = TimeSpan.FromMilliseconds(500),
                DecayDuration = TimeSpan.FromMilliseconds(800),
                RepetitionDelay = TimeSpan.FromMilliseconds(100),
                RepetitionMode = LampArrayRepetitionMode.Forever
            });

            playlists.Add(playlist);
        }

        LampArrayEffectPlaylist.StartAll(playlists);
        _currentPlaylists = playlists;
        _logger.LogInformation("Blink effect started");
    }

    public void StopAllEffects()
    {
        if (_currentPlaylists.Count > 0)
        {
            LampArrayEffectPlaylist.StopAll(_currentPlaylists);
            _currentPlaylists = Array.Empty<LampArrayEffectPlaylist>();
        }
    }

    public void StartAutoRGB()
    {
        if (_isAutoRGBEnabled)
            return;

        StopAllEffects();
        _isAutoRGBEnabled = true;
        _logger.LogInformation("AutoRGB mode started");
    }

    public void StopAutoRGB()
    {
        if (!_isAutoRGBEnabled)
            return;

        _isAutoRGBEnabled = false;
        _logger.LogInformation("AutoRGB mode stopped");
    }

    public void HandleAutoRGBColor(Color color)
    {
        if (_isAutoRGBEnabled)
        {
            ApplyColorToDevices(color);
            _currentColor = color;
            ColorChanged?.Invoke(this, color);
        }
    }

    public void RegisterInputProvider(ILightingInputProvider provider)
    {
        if (!_inputProviders.Contains(provider))
        {
            provider.ValueChanged += OnInputProviderValueChanged;
            _inputProviders.Add(provider);
            _logger.LogInformation($"Input provider registered: {provider.Name}");
        }
    }

    public void UnregisterInputProvider(ILightingInputProvider provider)
    {
        if (_inputProviders.Contains(provider))
        {
            provider.ValueChanged -= OnInputProviderValueChanged;
            provider.Stop();
            _inputProviders.Remove(provider);
            _logger.LogInformation($"Input provider unregistered: {provider.Name}");
        }
    }

    private void OnInputProviderValueChanged(object? sender, NumericValueChangedEventArgs e)
    {
        var color = ConvertValueToColor(e.Value);
        ApplyColorToDevices(color);
        _currentColor = color;
        ColorChanged?.Invoke(this, color);
    }

    private Color ConvertValueToColor(double value)
    {
        double normalizedValue = (value - 0) / 100;
        normalizedValue = Math.Clamp(normalizedValue, 0, 1);

        if (normalizedValue < 0.33)
        {
            double t = normalizedValue * 3;
            return Color.FromArgb(255, 0, (byte)(t * 255), 255);
        }
        else if (normalizedValue < 0.66)
        {
            double t = (normalizedValue - 0.33) * 3;
            return Color.FromArgb(255, (byte)(t * 255), 255, (byte)((1 - t) * 255));
        }
        else
        {
            double t = (normalizedValue - 0.66) * 3;
            return Color.FromArgb(255, 255, (byte)((1 - t) * 255), 0);
        }
    }
}