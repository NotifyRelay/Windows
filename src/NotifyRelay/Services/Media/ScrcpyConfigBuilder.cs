using NotifyRelay.Data.Contracts;
using NotifyRelay.Data.Enums;

namespace NotifyRelay.Services.Media;

/// <summary>
/// scrcpy 命令行参数构建（由 ScreenMirrorService.BuildScrcpyArguments 整体搬入）。
/// 纯函数式：输入 (args, deviceSerial, settings) → 输出 (参数串, deviceSerial)；
/// 不持有任何可变状态、不启动进程。
/// 唯一的外部副作用「终止该设备已存在的 scrcpy 进程」通过注入的委托完成，
/// 避免本类持有 scrcpy 进程字典（由 ScrcpyProcessManager 提供该委托）。
/// </summary>
internal sealed class ScrcpyConfigBuilder
{
    private readonly ILogger logger;
    private readonly IAdbService adbService;
    private readonly Action<string> killExistingProcess;

    public ScrcpyConfigBuilder(ILogger logger, IAdbService adbService, Action<string> killExistingProcess)
    {
        this.logger = logger;
        this.adbService = adbService;
        this.killExistingProcess = killExistingProcess;
    }

    public (string, string) Build(List<string> args, string deviceSerial, IDeviceSettingsService settings)
    {
        // Check if this is audio-only mode (either from settings or custom args)
        var isAudioOnlyMode = settings.DisableVideoForwarding || args.Any(arg => arg.Contains("--no-video"));

        if (isAudioOnlyMode)
        {
            // For audio-only mode, build minimal command
            // Note: Audio is enabled by default, so we don't need to add --audio explicitly
            var audioOnlyArgs = new List<string>
            {
                $"-s {deviceSerial}",
                "--no-video",
                "--audio-source=playback",
                "--audio-dup"
            };

            // Add optional audio settings
            if (!string.IsNullOrEmpty(settings.AudioBitrate))
            {
                audioOnlyArgs.Add($"--audio-bit-rate={settings.AudioBitrate}");
            }

            if (!string.IsNullOrEmpty(settings.AudioBuffer))
            {
                audioOnlyArgs.Add($"--audio-buffer={settings.AudioBuffer}");
            }

            if (!string.IsNullOrEmpty(settings.AudioOutputBuffer))
            {
                audioOnlyArgs.Add($"--audio-output-buffer={settings.AudioOutputBuffer}");
            }

            // Join and log the final command for debugging
            var finalArgs = string.Join(" ", audioOnlyArgs);
            logger.LogDebug("[调试] 仅音频模式 scrcpy 命令：{FinalArgs}", finalArgs);

            return (finalArgs, deviceSerial);
        }

        // Normal mode - use all settings
        var preDefinedArgs = settings.CustomArguments;

        if (!string.IsNullOrEmpty(preDefinedArgs))
        {
            args.Add(preDefinedArgs);
        }

        // General settings
        if (settings.ScreenOff)
        {
            args.Add("--turn-screen-off");
        }

        if (settings.PhysicalKeyboard)
        {
            args.Add("--keyboard=uhid");
        }

        // Video settings
        if (settings.VideoCodec != 0)
        {
            args.Add($"{adbService.VideoCodecOptions[settings.VideoCodec].Command}");
        }

        if (!string.IsNullOrEmpty(settings.VideoResolution))
        {
            args.Add($"--max-size={settings.VideoResolution}");
        }

        if (!string.IsNullOrEmpty(settings.VideoBitrate))
        {
            args.Add($"--video-bit-rate={settings.VideoBitrate}");
        }

        if (!string.IsNullOrEmpty(settings.VideoBuffer))
        {
            args.Add($"--video-buffer={settings.VideoBuffer}");
        }

        if (!string.IsNullOrEmpty(settings.FrameRate))
        {
            args.Add($"--max-fps={settings.FrameRate}");
        }

        if (!string.IsNullOrEmpty(settings.Crop))
        {
            args.Add($"--crop={settings.Crop}");
        }

        if (settings.DisplayOrientation != 0)
        {
            args.Add($"--orientation={adbService.DisplayOrientationOptions[settings.DisplayOrientation].Command}");
        }

        if (!string.IsNullOrEmpty(settings.Display))
        {
            args.Add($"--display-id={settings.Display}");
        }

        // Audio settings
        if (!string.IsNullOrEmpty(settings.AudioBitrate))
        {
            args.Add($"--audio-bit-rate={settings.AudioBitrate}");
        }

        if (!string.IsNullOrEmpty(settings.AudioBuffer))
        {
            args.Add($"--audio-buffer={settings.AudioBuffer}");
        }

        if (!string.IsNullOrEmpty(settings.AudioOutputBuffer))
        {
            args.Add($"--audio-output-buffer={settings.AudioOutputBuffer}");
        }

        if (settings.ForwardMicrophone)
        {
            args.Add("--audio-source=mic");
        }

        switch (settings.AudioOutputMode)
        {
            case AudioOutputModeType.Remote:
                args.Add("--no-audio");
                break;
            case AudioOutputModeType.Both:
                args.Add("--audio-dup");
                break;
        }

        if (settings.AudioCodec != 0)
        {
            args.Add($"{adbService.AudioCodecOptions[settings.AudioCodec].Command}");
        }

        if (args[0].StartsWith("--start-app"))
        {
            if (!string.IsNullOrEmpty(settings.VirtualDisplaySize) && settings.IsVirtualDisplayEnabled)
            {
                args.Add($"--new-display={settings.VirtualDisplaySize}");
            }
            else if (settings.IsVirtualDisplayEnabled)
            {
                args.Add("--new-display");
            }
            else
            {
                // Check for existing processes for this device and terminate them
                // when virtual display is not enabled
                killExistingProcess(deviceSerial);
            }
        }

        return (string.Join(" ", args), deviceSerial);
    }
}
