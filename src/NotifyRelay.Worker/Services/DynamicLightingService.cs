using Windows.Devices.Enumeration;
using Windows.Devices.Lights;
using Windows.Devices.Lights.Effects;
using Windows.UI;
using Microsoft.Extensions.Logging;
using NotifyRelay.Worker.Configuration;

namespace NotifyRelay.Worker.Services;

public class LampArrayDeviceDto
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public bool IsAvailable { get; set; }
    public int LampCount { get; set; }
    public string Kind { get; set; } = string.Empty;
}

public class DynamicLightingService
{
    private readonly ILogger _logger;
    private readonly WorkerConfiguration _config;
    private DeviceWatcher? _deviceWatcher;
    private readonly List<LampArrayDeviceInfo> _attachedDevices = [];
    private readonly object _devicesLock = new();
    private IReadOnlyList<LampArrayEffectPlaylist> _currentPlaylists = Array.Empty<LampArrayEffectPlaylist>();
    private bool _isAutoRGBEnabled;
    private double _brightness = 1.0;
    private Color _currentColor = new() { A = 255, R = 255, G = 255, B = 255 };
    private Color _manualColor;
    private Color _lastCapturedColor;
    private readonly Queue<Color> _colorWindow = new(5);
    private const int ColorWindowSize = 5;
    private ScreenColorAnalyzer? _screenColorAnalyzer;

    public event Action<Color>? ColorChanged;
    public event Action<Color>? CapturedColorChanged;
    public event Action? DevicesChanged;

    public bool IsAutoRGBEnabled => _isAutoRGBEnabled;
    public Color CurrentColor => _currentColor;

    public double Brightness
    {
        get => _brightness;
        set
        {
            _brightness = Math.Clamp(value, 0, 1);
            UpdateBrightnessOnDevices();
        }
    }

    public DynamicLightingService(ILogger logger, WorkerConfiguration config)
    {
        _logger = logger;
        _config = config;
    }

    public void Initialize()
    {
        try
        {
            LoadSettings();

            if (_config.EnableAutoRGB)
                StartAutoRGB();

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

    private void LoadSettings()
    {
        _brightness = _config.DynamicLightingBrightness;

        var colorString = _config.DynamicLightingColor;
        if (!string.IsNullOrEmpty(colorString) && colorString.StartsWith("#"))
        {
            try
            {
                var hex = colorString.TrimStart('#');
                var bytes = Convert.FromHexString(hex);
                if (bytes.Length == 4)
                    _currentColor = new Color { A = bytes[0], R = bytes[1], G = bytes[2], B = bytes[3] };
                else if (bytes.Length == 3)
                    _currentColor = new Color { A = 255, R = bytes[0], G = bytes[1], B = bytes[2] };
            }
            catch { }
        }

        _manualColor = _currentColor;
    }

    public void SetColorFromString(string hexColor)
    {
        try
        {
            var hex = hexColor.TrimStart('#');
            var bytes = Convert.FromHexString(hex);
            Color color;
            if (bytes.Length == 4)
                color = new Color { A = bytes[0], R = bytes[1], G = bytes[2], B = bytes[3] };
            else if (bytes.Length == 3)
                color = new Color { A = 255, R = bytes[0], G = bytes[1], B = bytes[2] };
            else return;

            _currentColor = color;
            _manualColor = color;
            if (!_isAutoRGBEnabled)
            {
                ApplyColorToDevices(color);
                NotifyColorChanged(color);
            }
        }
        catch { }
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

        lock (_devicesLock)
        {
            _attachedDevices.Clear();
        }
    }

    private async void DeviceWatcher_Added(DeviceWatcher sender, DeviceInformation args)
    {
        try
        {
            var lampArray = await LampArray.FromIdAsync(args.Id);
            if (lampArray == null)
            {
                _logger.LogWarning("Failed to initialize LampArray: {Name}", args.Name);
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

            lock (_devicesLock)
            {
                _attachedDevices.Add(deviceInfo);
                deviceInfo.LampArray!.BrightnessLevel = _brightness;
                deviceInfo.LampArray.SetColor(_currentColor);
            }

            NotifyDevicesChanged();
            _logger.LogInformation("LampArray added: {Name}", args.Name);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error adding LampArray: {Name}", args.Name);
        }
    }

    private void DeviceWatcher_Removed(DeviceWatcher sender, DeviceInformationUpdate args)
    {
        lock (_devicesLock)
        {
            var device = _attachedDevices.FirstOrDefault(d => d.Id == args.Id);
            if (device != null)
            {
                _attachedDevices.Remove(device);
                _logger.LogInformation("LampArray removed: {Name}", device.Name);
            }
        }
        NotifyDevicesChanged();
    }

    private void LampArray_AvailabilityChanged(LampArray sender, object args)
    {
        lock (_devicesLock)
        {
            var device = _attachedDevices.FirstOrDefault(d => d.LampArray == sender);
            if (device != null)
                device.IsAvailable = sender.IsAvailable;
        }
        NotifyDevicesChanged();
    }

    public void ApplyColorToDevices(Color color)
    {
        lock (_devicesLock)
        {
            foreach (var device in _attachedDevices)
            {
                if (device.IsAvailable && device.LampArray != null)
                {
                    try { device.LampArray.SetColor(color); }
                    catch (Exception ex) { _logger.LogError(ex, "Failed to apply color to {Name}", device.Name); }
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
        lock (_devicesLock)
        {
            foreach (var device in _attachedDevices)
            {
                if (device.IsAvailable && device.LampArray != null)
                {
                    try { device.LampArray.BrightnessLevel = _brightness; }
                    catch (Exception ex) { _logger.LogError(ex, "Failed to update brightness on {Name}", device.Name); }
                }
            }
        }
    }

    public void StartRainbowEffect()
    {
        StopAllEffects();
        var playlists = new List<LampArrayEffectPlaylist>();

        lock (_devicesLock)
        {
            foreach (var device in _attachedDevices)
            {
                if (!device.IsAvailable || device.LampArray == null) continue;

                var playlist = new LampArrayEffectPlaylist
                {
                    EffectStartMode = LampArrayEffectStartMode.Sequential,
                    RepetitionMode = LampArrayRepetitionMode.Forever
                };

                int[] indices = Enumerable.Range(0, device.LampCount).ToArray();

                playlist.Append(new LampArrayColorRampEffect(device.LampArray, indices)
                { Color = new Color { A = 255, R = 255, G = 0, B = 0 }, RampDuration = TimeSpan.FromMilliseconds(500), CompletionBehavior = LampArrayEffectCompletionBehavior.KeepState });
                playlist.Append(new LampArrayColorRampEffect(device.LampArray, indices)
                { Color = new Color { A = 255, R = 255, G = 255, B = 0 }, RampDuration = TimeSpan.FromMilliseconds(500), CompletionBehavior = LampArrayEffectCompletionBehavior.KeepState });
                playlist.Append(new LampArrayColorRampEffect(device.LampArray, indices)
                { Color = new Color { A = 255, R = 0, G = 255, B = 0 }, RampDuration = TimeSpan.FromMilliseconds(500), CompletionBehavior = LampArrayEffectCompletionBehavior.KeepState });
                playlist.Append(new LampArrayColorRampEffect(device.LampArray, indices)
                { Color = new Color { A = 255, R = 0, G = 0, B = 255 }, RampDuration = TimeSpan.FromMilliseconds(500), CompletionBehavior = LampArrayEffectCompletionBehavior.KeepState });

                playlists.Add(playlist);
            }
        }

        LampArrayEffectPlaylist.StartAll(playlists);
        _currentPlaylists = playlists;
        _logger.LogInformation("Rainbow effect started");
    }

    public void StartBlinkEffect(Color color)
    {
        StopAllEffects();
        var playlists = new List<LampArrayEffectPlaylist>();

        lock (_devicesLock)
        {
            foreach (var device in _attachedDevices)
            {
                if (!device.IsAvailable || device.LampArray == null) continue;

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
        if (_isAutoRGBEnabled) return;
        StopAllEffects();
        _manualColor = _currentColor;
        _isAutoRGBEnabled = true;

        try
        {
            _screenColorAnalyzer = new ScreenColorAnalyzer(_logger);
            _screenColorAnalyzer.ColorChanged += ScreenColorAnalyzer_ColorChanged;
            _screenColorAnalyzer.StartCapture();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to start AutoRGB");
            _isAutoRGBEnabled = false;
        }
    }

    public void StopAutoRGB()
    {
        if (!_isAutoRGBEnabled) return;
        _isAutoRGBEnabled = false;

        try
        {
            if (_screenColorAnalyzer != null)
            {
                _screenColorAnalyzer.ColorChanged -= ScreenColorAnalyzer_ColorChanged;
                _screenColorAnalyzer.StopCapture();
                _screenColorAnalyzer = null;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to stop AutoRGB");
        }

        ApplyColorToDevices(_manualColor);
        _currentColor = _manualColor;
        NotifyColorChanged(_manualColor);
    }

    private void ScreenColorAnalyzer_ColorChanged(object? sender, Color color)
    {
        _colorWindow.Enqueue(color);
        if (_colorWindow.Count > ColorWindowSize)
            _colorWindow.Dequeue();

        int totalR = 0, totalG = 0, totalB = 0;
        foreach (var c in _colorWindow)
        {
            totalR += c.R; totalG += c.G; totalB += c.B;
        }
        var avgColor = new Color
        {
            A = 255,
            R = (byte)(totalR / _colorWindow.Count),
            G = (byte)(totalG / _colorWindow.Count),
            B = (byte)(totalB / _colorWindow.Count)
        };

        if (avgColor.R == _lastCapturedColor.R &&
            avgColor.G == _lastCapturedColor.G &&
            avgColor.B == _lastCapturedColor.B)
            return;

        _lastCapturedColor = avgColor;
        HandleAutoRGBColor(avgColor);
        NotifyCapturedColorChanged(avgColor);
    }

    public void HandleAutoRGBColor(Color color)
    {
        if (_isAutoRGBEnabled)
            ApplyColorToDevices(color);
    }

    public List<LampArrayDeviceDto> GetDevices()
    {
        lock (_devicesLock)
        {
            return _attachedDevices.Select(d => new LampArrayDeviceDto
            {
                Id = d.Id,
                Name = d.Name,
                IsAvailable = d.IsAvailable,
                LampCount = d.LampCount,
                Kind = d.Kind.ToString()
            }).ToList();
        }
    }

    private void NotifyColorChanged(Color color)
    {
        ColorChanged?.Invoke(color);
    }

    private void NotifyCapturedColorChanged(Color color)
    {
        CapturedColorChanged?.Invoke(color);
    }

    private void NotifyDevicesChanged()
    {
        DevicesChanged?.Invoke();
    }
}
