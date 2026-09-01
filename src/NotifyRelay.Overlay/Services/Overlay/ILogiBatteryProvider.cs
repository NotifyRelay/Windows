using NotifyRelay.Models.Render;

namespace NotifyRelay.Services.Overlay;

/// <summary>
/// 罗技电池设备数据提供者。
/// 由主项目 LogiBatteryProvider（通过 Rust FFI 获取数据）实现，注入到 OverlayRenderService。
/// </summary>
public interface ILogiBatteryProvider
{
    /// <summary>获取当前所有罗技设备的电池快照。</summary>
    IReadOnlyList<LogiBatteryDeviceInfo> GetDevices();

    /// <summary>设备列表/电量数据变化时触发（通常来自 FFI 回调或后台轮询）。</summary>
    event EventHandler? DevicesUpdated;

    /// <summary>启动后台监控（轮询/订阅回调）；未启用叠加层开关时可暂停。</summary>
    void StartMonitoring();

    /// <summary>停止后台监控。</summary>
    void StopMonitoring();
}
