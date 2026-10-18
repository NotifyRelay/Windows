using NotifyRelay.Data.Items;

namespace NotifyRelay.Services.Adb;

/// <summary>
/// Scrcpy 偏好选项集合（静态只读语义）。
/// 索引顺序与 Command 字符串必须与拆分前逐字一致：
/// ScreenMirrorService 直接按索引值下标访问这些集合。
/// </summary>
public static class ScrcpyPreferences
{
    // Initialize the codec option collections
    public static ObservableCollection<ScrcpyPreferenceItem> DisplayOrientation { get; } =
    [
        new(0, "", "Default"),
        new(1, "0", "0°"),
        new(2, "90", "90°"),
        new(3, "180", "180°"),
        new(4, "270", "270°"),
        new(5, "flip0", "flip-0°"),
        new(6, "flip90", "flip-90°"),
        new(7, "flip180", "flip-180°"),
        new(8, "flip270", "flip-270°")
    ];

    public static ObservableCollection<ScrcpyPreferenceItem> VideoCodec { get; } =
    [
        new(0, "", "Default"),
        new(1, "--video-codec=h264 --video-encoder=OMX.qcom.video.encoder.avc", "h264 & c2.qti.avc.encoder (hw)"),
        new(2, "--video-codec=h264 --video-encoder=c2.android.avc.encoder", "h264 & c2.android.avc.encoder (sw)"),
        new(4, "--video-codec=h264 --video-encoder=OMX.google.h264.encoder", "h264 & OMX.google.h264.encoder (sw)"),
        new(5, "--video-codec=h265 --video-encoder=OMX.qcom.video.encoder.hevc", "h265 & OMX.qcom.video.encoder.hevc (hw)"),
        new(6, "--video-codec=h265 --video-encoder=c2.android.hevc.encoder", "h265 & c2.android.hevc.encoder (sw)")
    ];

    public static ObservableCollection<ScrcpyPreferenceItem> AudioCodec { get; } =
    [
        new(0, "", "Default"),
        new(1, "--audio-codec=opus --audio-encoder=c2.android.opus.encoder", "opus & c2.android.opus.encoder (sw)"),
        new(2, "--audio-codec=aac --audio-encoder=c2.android.aac.encoder", "aac & c2.android.aac.encoder (sw)"),
        new(3, "--audio-codec=aac --audio-encoder=OMX.google.aac.encoder", "aac & OMX.google.aac.encoder (sw)"),
        new(4, "--audio-codec=raw", "raw")
    ];
}
