using NotifyRelay.Models.Render;

namespace NotifyRelay.Services.Overlay;

/// <summary>
/// 叠加层输入契约：宿主把渲染所需数据推入渲染服务，渲染服务不反向引用宿主类型。
/// </summary>
public interface IOverlayDataSink
{
    /// <summary>启动渲染线程与看门狗。</summary>
    void Start();

    /// <summary>停止渲染线程并清理覆盖层资源。</summary>
    void Stop();

    /// <summary>每次启动完成后触发（含看门狗重启），供宿主重推数据快照。</summary>
    event EventHandler? Started;

    /// <summary>推送键盘状态快照。</summary>
    void ApplyKeyboardState(KeyboardStateSnapshot snapshot);

    /// <summary>推送快捷键映射提示文本。</summary>
    void ShowKeyMappingHint(string text);

    /// <summary>推送罗技电池设备快照；空集合表示无设备。</summary>
    void ApplyLogiBatteryDevices(IReadOnlyList<LogiBatteryDeviceInfo> devices);

    /// <summary>更新时间浮窗配置（启用、目标屏、位置百分比、颜色、描边、缩放、格式）。</summary>
    void SetClockConfig(bool enabled, string targetScreen, float xPct, float yPct,
        string colorHex, float outlineWidth, float scale, bool showSeconds, bool use24Hour);

    /// <summary>更新心率覆盖层配置（启用、样式组合、目标屏、位置百分比、颜色）。</summary>
    void SetHeartRateConfig(bool enabled, int styleFlags, string targetScreen, float xPct, float yPct,
        string colorHex, float outlineWidth, float scale, bool alertEnabled, int lowAlert, int highAlert,
        int spikeDelta, bool hideWhenDisconnected = true);

    /// <summary>推送最新心率值。</summary>
    void UpdateHeartRate(int bpm);

    /// <summary>设置心率设备连接状态。</summary>
    void SetHeartRateConnected(bool connected);

    /// <summary>清空心率显示数据。</summary>
    void ClearHeartRate();

    /// <summary>更新 DeepSeek 余额叠加层配置（启用、目标屏、位置百分比、缩放）。</summary>
    void SetDeepSeekBalanceConfig(bool enabled, string targetScreen, float xPct, float yPct, float scale);

    /// <summary>推送最新余额与相对上一条记录的变化量；balance 传 null 表示暂无数据。</summary>
    void UpdateDeepSeekBalance(double? balance, double change);
}
