using System.Collections.ObjectModel;
using Microsoft.UI.Dispatching;
using NotifyRelay.Data.Contracts;
using NotifyRelay.Models.Render;
using NotifyRelay.Services.Infrastructure;
using NotifyRelay.Services.Overlay;

namespace NotifyRelay.ViewModels.Settings;

/// <summary>
/// 叠加层子设置页 - 罗技电池 ViewModel。
/// 包装 6 个设置项 + 实时设备列表（与设备数据源事件同步）。
/// </summary>
public class LogiBatteryViewModel : ObservableViewModelBase
{
    private readonly IGeneralSettingsService _settings;
    private readonly ILogiBatteryDeviceSource? _provider;
    private readonly IScreenCatalog? _screenCatalog;

    public ObservableCollection<LogiBatteryDeviceInfo> Devices { get; } = [];

    public List<ScreenOption> Screens { get; } = [];

    private ScreenOption? _selectedScreen;
    public ScreenOption? SelectedScreen
    {
        get => _selectedScreen;
        set
        {
            _selectedScreen = value;
            if (value != null) _settings.LogiBatteryTargetScreen = value.Id;
            OnPropertyChanged();
        }
    }

    // ===== 6 个设置属性（包装 IGeneralSettingsService 并触发渲染刷新） =====

    public bool LogiBatteryEnabled
    {
        get => _settings.LogiBatteryEnabled;
        set
        {
            _settings.LogiBatteryEnabled = value;
            OnPropertyChanged();
            if (value) _provider?.StartMonitoring();
            else _provider?.StopMonitoring();
        }
    }

    public int XPercent
    {
        get => _settings.LogiBatteryXPercent;
        set { _settings.LogiBatteryXPercent = value; OnPropertyChanged(); }
    }

    public int YPercent
    {
        get => _settings.LogiBatteryYPercent;
        set { _settings.LogiBatteryYPercent = value; OnPropertyChanged(); }
    }

    public float Scale
    {
        get => _settings.LogiBatteryScale;
        set { _settings.LogiBatteryScale = value; OnPropertyChanged(); }
    }

    public bool HideWhenDisconnected
    {
        get => _settings.LogiBatteryHideWhenDisconnected;
        set { _settings.LogiBatteryHideWhenDisconnected = value; OnPropertyChanged(); }
    }

    private string? _statusHint;
    /// <summary>加载状态 / 错误提示（显示在页面上方）。</summary>
    public string? StatusHint
    {
        get => _statusHint;
        private set { _statusHint = value; OnPropertyChanged(); }
    }

    public LogiBatteryViewModel()
    {
        _settings = Ioc.Default.GetRequiredService<IGeneralSettingsService>();
        _provider = Ioc.Default.GetService<ILogiBatteryDeviceSource>();
        _screenCatalog = Ioc.Default.GetService<IScreenCatalog>();
        Dispatcher = DispatcherQueue.GetForCurrentThread();

        BuildScreenOptions();

        // 订阅设备数据源事件，同步 Devices 集合
        if (_provider != null)
        {
            _provider.DevicesUpdated += OnDevicesUpdated;
            // 首次进入页面，立即刷新一次当前快照
            OnDevicesUpdated(_provider, EventArgs.Empty);
            if (!_provider.IsReady)
                StatusHint = "logi_battery.dll 未就绪，请确认构建输出目录已复制该 DLL。";
        }
        else
        {
            StatusHint = "服务未注册（设备数据源 DI 未配置）。";
        }
    }

    public void ForceRefreshNow()
    {
        if (_provider == null) return;
        if (!_provider.IsReady)
        {
            StatusHint = "logi_battery.dll 未就绪，无法刷新。";
            return;
        }
        _provider.StartMonitoring(); // 确保轮询已启动（会触发立即刷新）
        _ = _provider.RefreshNowAsync();
    }

    private void BuildScreenOptions()
    {
        _selectedScreen = OverlayScreenOptions.BuildInto(Screens, _screenCatalog, _settings.LogiBatteryTargetScreen);
    }

    private void OnDevicesUpdated(object? sender, EventArgs e)
    {
        if (_provider == null) return;
        RunOnUi(() =>
        {
            var snapshot = _provider.GetDevices();
            Devices.Clear();
            foreach (var d in snapshot) Devices.Add(d);

            if (Devices.Count == 0)
                StatusHint = _provider.IsReady
                    ? "未检测到罗技设备（未配对、HID 未授权 或 设备离线）。"
                    : "logi_battery.dll 未就绪，请确认构建输出目录已复制该 DLL。";
            else
                StatusHint = null;
        });
    }
}
