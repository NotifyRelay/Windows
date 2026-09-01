using NotifyRelay.Data.Contracts;
using NotifyRelay.Services.HeartRate;
using NotifyRelay.Services.Overlay;

namespace NotifyRelay.Services.OverlayFeatures;

/// <summary>
/// 心率叠加层功能：开启"启动时自动连接"时，应用启动后自动连接上次连接过的心率设备。
/// 心率数据由设置页把 BLE 服务桥接到叠加层，因此这里无需绑定数据源。
/// 登记到容器后由叠加层模块主初始化自动引导，无需在启动流程中手写。
/// </summary>
public sealed class HeartRateOverlayFeature : IOverlayFeature
{
    private readonly HeartRateBleService _bleService;
    private readonly IGeneralSettingsService _settings;

    public HeartRateOverlayFeature(HeartRateBleService bleService, IGeneralSettingsService settings)
    {
        _bleService = bleService;
        _settings = settings;
    }

    public string Name => "心率设备启动自动连接";

    public bool IsEnabled => _settings.HeartRateAutoConnectEnabled;

    /// <summary>心率数据不经 Provider 注入（由设置页桥接），无需绑定。</summary>
    public void Bind(OverlayRenderService renderService)
    {
    }

    public void Start() => _bleService.TryAutoConnectOnStartup();

    public void Stop()
    {
        // 自动连接是"一次性"的后台重连循环，停止时无需额外处理；
        // 用户主动断开由 BLE 服务自身维护会话内记忆，此处不做干预。
    }
}
