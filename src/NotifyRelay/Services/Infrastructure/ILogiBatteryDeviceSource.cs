using NotifyRelay.Models.Render;

namespace NotifyRelay.Services.Infrastructure;

/// <summary>
/// 罗技电池设备数据源契约：设备快照 + 变化通知 + 监控启停 + 手动刷新。
/// </summary>
public interface ILogiBatteryDeviceSource
{
    /// <summary>底层原生库是否就绪（DLL 已加载）。</summary>
    bool IsReady { get; }

    /// <summary>获取当前所有罗技设备的电池快照。</summary>
    IReadOnlyList<LogiBatteryDeviceInfo> GetDevices();

    /// <summary>设备列表/电量数据变化时触发。</summary>
    event EventHandler? DevicesUpdated;

    /// <summary>启动后台监控。</summary>
    void StartMonitoring();

    /// <summary>停止后台监控。</summary>
    void StopMonitoring();

    /// <summary>立即刷新一次设备快照（不改变监控状态）。</summary>
    Task RefreshNowAsync();
}
