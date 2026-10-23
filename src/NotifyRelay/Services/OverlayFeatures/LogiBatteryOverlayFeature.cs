using NotifyRelay.Data.Contracts;
using NotifyRelay.Services.Infrastructure;
using NotifyRelay.Services.Overlay;

namespace NotifyRelay.Services.OverlayFeatures;

/// <summary>
/// 罗技电池叠加层功能：把电池设备快照推送到叠加层，
/// 并在开关开启时启动后台 HID 轮询监控。
/// 登记到容器后由 OverlayFeatureHost 统一引导，无需在启动流程中手写。
/// </summary>
public sealed class LogiBatteryOverlayFeature : IOverlayHostFeature
{
    private readonly ILogiBatteryDeviceSource _provider;
    private readonly IGeneralSettingsService _settings;

    private IOverlayDataSink? _sink;

    public LogiBatteryOverlayFeature(ILogiBatteryDeviceSource provider, IGeneralSettingsService settings)
    {
        _provider = provider;
        _settings = settings;
    }

    public string Name => "罗技电池叠加层";

    public bool IsEnabled => _settings.LogiBatteryEnabled;

    /// <summary>
    /// 订阅设备变化事件并推送一次当前快照。
    /// 幂等：看门狗重启会再次调用本方法，先退订再订阅，避免事件重复挂钩。
    /// </summary>
    public void Attach(IOverlayDataSink sink)
    {
        _sink = sink;
        _provider.DevicesUpdated -= OnDevicesUpdated;
        _provider.DevicesUpdated += OnDevicesUpdated;
        sink.ApplyLogiBatteryDevices(_provider.GetDevices());
    }

    public void Start() => _provider.StartMonitoring();

    public void Stop() => _provider.StopMonitoring();

    private void OnDevicesUpdated(object? sender, EventArgs e)
        => _sink?.ApplyLogiBatteryDevices(_provider.GetDevices());
}
