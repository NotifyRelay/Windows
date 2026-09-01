using NotifyRelay.Data.Models;

namespace NotifyRelay.Data.Contracts;

public interface IScreenMirrorService : IDisposable
{
    Task<bool> StartScrcpy(PairedDevice device, string? customArgs = null, string? iconPath = null);
    void StopScrcpy(string deviceSerial);
    void StopScrcpyByDeviceId(string deviceId);
    bool IsAudioOnlyRunning(string deviceId);

    /// <summary>
    /// 处理音频转发请求
    /// </summary>
    Task ProcessAudioRequestAsync(PairedDevice device);
}
