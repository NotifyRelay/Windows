using System.Collections.ObjectModel;
using System.Runtime.CompilerServices;
using Microsoft.UI.Dispatching;
using NotifyRelay.Data.Contracts;
using NotifyRelay.Services.HeartRate;
using NotifyRelay.Services.Overlay;

namespace NotifyRelay.ViewModels.Settings;

/// <summary>心率子页下拉框中的屏幕选项。</summary>
public sealed class ScreenOption
{
    public string Id { get; init; } = "PRIMARY";
    public string DisplayName { get; init; } = string.Empty;
    public override string ToString() => DisplayName;
}

/// <summary>
/// 心率设置子页 ViewModel：BLE 扫描/连接/断开、显示开关、样式组合、
/// 目标屏幕与 X/Y 位置，并把配置与实时心率推送到覆盖层渲染服务。
/// </summary>
public class HeartRateViewModel : INotifyPropertyChanged
{
    private readonly IGeneralSettingsService _settings;
    private readonly OverlayRenderService? _renderService;
    private readonly HeartRateBleService _bleService;
    private DispatcherQueue? _dispatcher;

    public event PropertyChangedEventHandler? PropertyChanged;

    public ObservableCollection<HeartRateDeviceInfo> Devices { get; } = [];

    public List<ScreenOption> Screens { get; } = [];

    private HeartRateDeviceInfo? _selectedDevice;
    public HeartRateDeviceInfo? SelectedDevice
    {
        get => _selectedDevice;
        set { _selectedDevice = value; OnPropertyChanged(); OnPropertyChanged(nameof(CanConnect)); }
    }

    private HeartRateConnectionState _state = HeartRateConnectionState.Disconnected;
    public HeartRateConnectionState State
    {
        get => _state;
        private set
        {
            _state = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(StatusText));
            OnPropertyChanged(nameof(IsConnected));
            OnPropertyChanged(nameof(IsScanning));
            OnPropertyChanged(nameof(CanConnect));
        }
    }

    public bool IsConnected => _state == HeartRateConnectionState.Connected;
    public bool IsScanning => _state == HeartRateConnectionState.Scanning;
    public bool CanConnect => SelectedDevice != null
        && _state != HeartRateConnectionState.Connecting
        && _state != HeartRateConnectionState.Connected;

    private int _currentBpm;
    public int CurrentBpm
    {
        get => _currentBpm;
        private set { _currentBpm = value; OnPropertyChanged(); OnPropertyChanged(nameof(StatusText)); }
    }

    public string StatusText => _state switch
    {
        HeartRateConnectionState.Scanning => "正在扫描心率设备…",
        HeartRateConnectionState.Connecting => "正在连接…",
        HeartRateConnectionState.Connected => _currentBpm > 0 ? $"已连接 · {_currentBpm} BPM" : "已连接，等待数据…",
        _ => "未连接"
    };

    // ===== 显示设置（持久化 + 推送渲染层） =====

    public bool HeartRateOverlayEnabled
    {
        get => _settings.HeartRateOverlayEnabled;
        set { _settings.HeartRateOverlayEnabled = value; OnPropertyChanged(); PushConfig(); }
    }

    public bool StyleTextEnabled
    {
        get => (_settings.HeartRateStyle & 1) != 0;
        set { SetStyleFlag(1, value); OnPropertyChanged(); }
    }

    public bool StyleCardEnabled
    {
        get => (_settings.HeartRateStyle & 2) != 0;
        set { SetStyleFlag(2, value); OnPropertyChanged(); }
    }

    public bool StyleHeartEnabled
    {
        get => (_settings.HeartRateStyle & 4) != 0;
        set { SetStyleFlag(4, value); OnPropertyChanged(); }
    }

    private void SetStyleFlag(int flag, bool enabled)
    {
        int flags = _settings.HeartRateStyle;
        flags = enabled ? flags | flag : flags & ~flag;
        _settings.HeartRateStyle = flags;
        PushConfig();
    }

    private ScreenOption? _selectedScreen;
    public ScreenOption? SelectedScreen
    {
        get => _selectedScreen;
        set
        {
            _selectedScreen = value;
            if (value != null)
                _settings.HeartRateTargetScreen = value.Id;
            OnPropertyChanged();
            PushConfig();
        }
    }

    public int XPercent
    {
        get => _settings.HeartRateXPercent;
        set { _settings.HeartRateXPercent = value; OnPropertyChanged(); PushConfig(); }
    }

    public int YPercent
    {
        get => _settings.HeartRateYPercent;
        set { _settings.HeartRateYPercent = value; OnPropertyChanged(); PushConfig(); }
    }

    public string HeartRateColor
    {
        get => _settings.HeartRateColor;
        set { _settings.HeartRateColor = value; OnPropertyChanged(); PushConfig(); }
    }

    /// <summary>简洁文本描边粗细（像素，0~6，0 为无描边）。</summary>
    public float HeartRateTextOutlineWidth
    {
        get => _settings.HeartRateTextOutlineWidth;
        set { _settings.HeartRateTextOutlineWidth = value; OnPropertyChanged(); PushConfig(); }
    }

    public HeartRateViewModel()
    {
        _settings = Ioc.Default.GetRequiredService<IGeneralSettingsService>();
        _renderService = Ioc.Default.GetService<OverlayRenderService>();
        _bleService = Ioc.Default.GetRequiredService<HeartRateBleService>();
        _dispatcher = DispatcherQueue.GetForCurrentThread();

        BuildScreenOptions();

        _bleService.DeviceDiscovered += OnDeviceDiscovered;
        _bleService.StateChanged += OnBleStateChanged;
        _bleService.HeartRateReceived += OnHeartRateReceived;

        // 同步当前服务状态（页面重进时保持一致）
        _state = _bleService.State;
    }

    private void BuildScreenOptions()
    {
        Screens.Clear();
        Screens.Add(new ScreenOption { Id = "PRIMARY", DisplayName = "主显示器" });
        try
        {
            var list = _renderService?.GetScreenList();
            if (list != null)
            {
                int index = 1;
                foreach (var (deviceName, isPrimary) in list)
                {
                    Screens.Add(new ScreenOption
                    {
                        Id = deviceName,
                        DisplayName = $"显示器 {index}{(isPrimary ? " (主)" : "")} · {deviceName}"
                    });
                    index++;
                }
            }
        }
        catch
        {
            // 枚举失败时仅保留主显示器选项
        }

        var saved = _settings.HeartRateTargetScreen;
        _selectedScreen = Screens.FirstOrDefault(s => s.Id == saved) ?? Screens[0];
    }

    // ===== 命令 =====

    public void StartScan()
    {
        Devices.Clear();
        SelectedDevice = null;
        _bleService.StartScan();
    }

    public void StopScan() => _bleService.StopScan();

    public async Task ConnectSelectedAsync()
    {
        var device = SelectedDevice;
        if (device == null) return;
        await _bleService.ConnectAsync(device.Address);
    }

    public void Disconnect() => _bleService.Disconnect();

    // ===== BLE 事件（可能来自非 UI 线程） =====

    private void OnDeviceDiscovered(HeartRateDeviceInfo info)
    {
        RunOnUi(() =>
        {
            if (Devices.All(d => d.Address != info.Address))
                Devices.Add(info);
        });
    }

    private void OnBleStateChanged(HeartRateConnectionState state)
    {
        // 渲染层直接更新（线程安全）
        _renderService?.SetHeartRateConnected(state == HeartRateConnectionState.Connected);
        RunOnUi(() =>
        {
            State = state;
            if (state != HeartRateConnectionState.Connected)
                CurrentBpm = 0;
        });
    }

    private void OnHeartRateReceived(int bpm)
    {
        // 渲染层直接更新（线程安全）
        _renderService?.UpdateHeartRate(bpm);
        RunOnUi(() => CurrentBpm = bpm);
    }

    private void RunOnUi(Action action)
    {
        var dispatcher = _dispatcher ??= DispatcherQueue.GetForCurrentThread();
        if (dispatcher != null && !dispatcher.HasThreadAccess)
            dispatcher.TryEnqueue(() => action());
        else if (dispatcher != null)
            action();
    }

    /// <summary>把当前显示配置推送到渲染服务。</summary>
    private void PushConfig()
    {
        _renderService?.SetHeartRateConfig(
            _settings.HeartRateOverlayEnabled,
            _settings.HeartRateStyle,
            _settings.HeartRateTargetScreen,
            _settings.HeartRateXPercent,
            _settings.HeartRateYPercent,
            _settings.HeartRateColor,
            _settings.HeartRateTextOutlineWidth);
    }

    protected void OnPropertyChanged([CallerMemberName] string propertyName = "")
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
