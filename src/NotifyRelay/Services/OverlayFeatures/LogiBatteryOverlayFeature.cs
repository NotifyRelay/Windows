using NotifyRelay.Data.Contracts;
using NotifyRelay.Services.Overlay;

namespace NotifyRelay.Services.OverlayFeatures;

/// <summary>
/// 罗技电池叠加层功能：把电池数据提供者注入叠加层，
/// 并在开关开启时启动后台 HID 轮询监控。
/// 登记到容器后由叠加层模块主初始化自动引导，无需在启动流程中手写。
/// </summary>
public sealed class LogiBatteryOverlayFeature : IOverlayFeature
{
    private readonly ILogiBatteryProvider _provider;
    private readonly IGeneralSettingsService _settings;

    public LogiBatteryOverlayFeature(ILogiBatteryProvider provider, IGeneralSettingsService settings)
    {
        _provider = provider;
        _settings = settings;
    }

    public string Name => "罗技电池叠加层";

    public bool IsEnabled => _settings.LogiBatteryEnabled;

    public void Bind(OverlayRenderService renderService)
        => renderService.SetLogiBatteryProvider(_provider);

    public void Start() => _provider.StartMonitoring();

    public void Stop() => _provider.StopMonitoring();
}
