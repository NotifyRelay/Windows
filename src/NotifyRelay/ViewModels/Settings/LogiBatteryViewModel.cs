using System.Collections.ObjectModel;
using System.Runtime.CompilerServices;
using Microsoft.UI.Dispatching;
using NotifyRelay.Data.Contracts;
using NotifyRelay.Models.Render;
using NotifyRelay.Native;
using NotifyRelay.Services;
using NotifyRelay.Services.Overlay;

namespace NotifyRelay.ViewModels.Settings;

/// <summary>
/// 叠加层子设置页 - 罗技电池 ViewModel。
/// 包装 6 个设置项 + 实时设备列表（与 LogiBatteryProvider 事件同步）。
/// </summary>
public class LogiBatteryViewModel : INotifyPropertyChanged
{
    private readonly IGeneralSettingsService _settings;
    private readonly ILogiBatteryProvider? _provider;
    private readonly OverlayRenderService? _renderService;
    private DispatcherQueue? _dispatcher;

    public event PropertyChangedEventHandler? PropertyChanged;

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
        _provider = Ioc.Default.GetService<ILogiBatteryProvider>();
        _renderService = Ioc.Default.GetService<OverlayRenderService>();
        _dispatcher = DispatcherQueue.GetForCurrentThread();

        BuildScreenOptions();

        // 订阅 Provider 事件，同步 Devices 集合
        if (_provider != null)
        {
            _provider.DevicesUpdated += OnDevicesUpdated;
            // 首次进入页面，立即刷新一次当前快照
            OnDevicesUpdated(_provider, EventArgs.Empty);
            if (_provider is LogiBatteryProvider real && !LogiBatteryLoader.IsAvailable)
                StatusHint = "logi_battery.dll 未就绪，请确认构建输出目录已复制该 DLL。";
        }
        else
        {
            StatusHint = "服务未注册（LogiBatteryProvider DI 未配置）。";
        }
    }

    public void ForceRefreshNow()
    {
        if (_provider == null) return;
        if (!LogiBatteryLoader.IsAvailable)
        {
            StatusHint = "logi_battery.dll 未就绪，无法刷新。";
            return;
        }
        _provider.StartMonitoring(); // 确保轮询已启动（会触发立即刷新）
        // 兼容显式手动刷新：通过公共接口 Provider 内部 Task.Run 触发一次
        if (_provider is LogiBatteryProvider real)
            _ = real.RefreshOnceForPreviewAsync();
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
                int i = 1;
                foreach (var (deviceName, isPrimary) in list)
                {
                    Screens.Add(new ScreenOption
                    {
                        Id = deviceName,
                        DisplayName = $"显示器 {i}{(isPrimary ? " (主)" : "")} · {deviceName}"
                    });
                    i++;
                }
            }
        }
        catch { /* 忽略枚举异常 */ }

        var saved = _settings.LogiBatteryTargetScreen;
        _selectedScreen = Screens.FirstOrDefault(s =>
            string.Equals(s.Id, saved, StringComparison.OrdinalIgnoreCase)) ?? Screens[0];
    }

    private void OnDevicesUpdated(object? sender, EventArgs e)
    {
        if (_provider == null) return;
        RunOnUi(() =>
        {
            var snapshot = _provider.GetDevices();
            Devices.Clear();
            foreach (var d in snapshot) Devices.Add(d);

            if (Devices.Count == 0 && _provider is LogiBatteryProvider real)
                StatusHint = LogiBatteryLoader.IsAvailable
                    ? "未检测到罗技设备（未配对、HID 未授权 或 设备离线）。"
                    : "logi_battery.dll 未就绪，请确认构建输出目录已复制该 DLL。";
            else
                StatusHint = null;
        });
    }

    private void RunOnUi(Action action)
    {
        var dispatcher = _dispatcher ??= DispatcherQueue.GetForCurrentThread();
        if (dispatcher != null && !dispatcher.HasThreadAccess)
            dispatcher.TryEnqueue(() => action());
        else if (dispatcher != null)
            action();
    }

    protected void OnPropertyChanged([CallerMemberName] string propertyName = "") =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}

/// <summary>LogiBatteryProvider 的小扩展方法：设置页手动刷新按钮使用。</summary>
file static class LogiBatteryProviderRefreshExtensions
{
    public static Task RefreshOnceForPreviewAsync(this LogiBatteryProvider provider)
    {
        // 调用反射绕过 private，或调用 StartMonitoring 都会有效。这里选择简单反射：
        var mi = typeof(LogiBatteryProvider).GetMethod("RefreshOnceAsync",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        if (mi == null) return Task.CompletedTask;
        return (Task?)mi.Invoke(provider, null) ?? Task.CompletedTask;
    }
}
