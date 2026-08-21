namespace NotifyRelay.Services.Overlay;

/// <summary>
/// 覆盖层渲染服务所需的设置契约（弹幕样式 + 心率覆盖层）。
/// 由主程序设置服务实现，使覆盖层渲染不依赖主程序程序集。
/// </summary>
public interface IOverlaySettings
{
    int DanmakuFontSizePercent { get; set; }
    int DanmakuSpeed { get; set; }
    int DanmakuOpacityPercent { get; set; }
    int DanmakuDisplayAreaPercent { get; set; }
    int DanmakuDensity { get; set; }
    string DanmakuFontFamily { get; set; }
    bool DanmakuBold { get; set; }
    string DanmakuColor { get; set; }
    bool DanmakuBorderEnabled { get; set; }
    int DanmakuBorderThickness { get; set; }
    string DanmakuBorderColor { get; set; }
    bool DanmakuShadowEnabled { get; set; }
    int DanmakuShadowDepth { get; set; }
    int DanmakuShadowOpacity { get; set; }
    string DanmakuShadowColor { get; set; }
    int DanmakuDisplayScreenMode { get; set; }
    int DanmakuPerformanceMode { get; set; }

    bool HeartRateOverlayEnabled { get; set; }
    int HeartRateStyle { get; set; }
    string HeartRateTargetScreen { get; set; }
    int HeartRateXPercent { get; set; }
    int HeartRateYPercent { get; set; }
    string HeartRateColor { get; set; }
    float HeartRateTextOutlineWidth { get; set; }
    bool HeartRateAlertEnabled { get; set; }
    int HeartRateLowAlert { get; set; }
    int HeartRateHighAlert { get; set; }
    int HeartRateSpikeDelta { get; set; }
    float HeartRateScale { get; set; }
    bool HeartRateHideWhenDisconnected { get; set; }
}
