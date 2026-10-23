using NotifyRelay.Worker.Services;

namespace NotifyRelay.Worker.Contracts;

/// <summary>动态光效工具契约：命令 + 状态快照 + 事件，事件参数不携带平台类型。</summary>
public interface IDynamicLightingTool
{
    /// <summary>自动取色（AutoRGB）是否运行中。</summary>
    bool IsAutoRGBEnabled { get; }

    /// <summary>当前颜色。</summary>
    WorkerColor CurrentColor { get; }

    /// <summary>亮度（0~1），设置后立即下发到设备。</summary>
    double Brightness { get; set; }

    /// <summary>当前颜色变化。</summary>
    event EventHandler<WorkerColor>? ColorChanged;

    /// <summary>屏幕捕获颜色变化。</summary>
    event EventHandler<WorkerColor>? CapturedColorChanged;

    /// <summary>设备集合变化。</summary>
    event EventHandler? DevicesChanged;

    /// <summary>初始化设备监视与已保存效果。</summary>
    void Initialize();

    /// <summary>停止效果、释放设备监视。</summary>
    void Cleanup();

    /// <summary>按十六进制字符串（#RGB / #ARGB）设置颜色。</summary>
    void SetColorFromString(string hexColor);

    /// <summary>把颜色下发到所有可用设备。</summary>
    void ApplyColorToDevices(WorkerColor color);

    /// <summary>关闭所有设备灯效。</summary>
    void TurnOffAllDevices();

    /// <summary>把当前亮度下发到设备。</summary>
    void UpdateBrightnessOnDevices();

    /// <summary>启动彩虹效果。</summary>
    void StartRainbowEffect();

    /// <summary>启动闪烁效果。</summary>
    void StartBlinkEffect(WorkerColor color);

    /// <summary>停止所有效果。</summary>
    void StopAllEffects();

    /// <summary>启动自动取色。</summary>
    void StartAutoRGB();

    /// <summary>停止自动取色；restoreManualColor 为 true 时恢复手动颜色。</summary>
    void StopAutoRGB(bool restoreManualColor = true);

    /// <summary>自动取色回调：运行中时把颜色下发到设备。</summary>
    void HandleAutoRGBColor(WorkerColor color);

    /// <summary>当前设备快照。</summary>
    List<LampArrayDeviceDto> GetDevices();
}
