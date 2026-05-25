using AdvancedSharpAdbClient;
using AdvancedSharpAdbClient.Models;
using NotifyRelay.Data.Items;
using NotifyRelay.Data.Models;

namespace NotifyRelay.Data.Contracts;

public interface IAdbService
{
    ObservableCollection<AdbDevice> AdbDevices { get; }
    ObservableCollection<ScrcpyPreferenceItem> DisplayOrientationOptions { get; }
    ObservableCollection<ScrcpyPreferenceItem> VideoCodecOptions { get; }
    ObservableCollection<ScrcpyPreferenceItem> AudioCodecOptions { get; }
    Task StartAsync();
    Task<bool> ConnectWireless(string? host, int port = 5555);
    Task StopAsync();
    Task UninstallApp(string deviceId, string appPackage);
    void UnlockDevice(DeviceData deviceData, List<string> unlockCommands);
    bool IsMonitoring { get; }
    AdbClient AdbClient { get; }
    void TryConnectTcp(string host);

    Task<bool> IsLocked(DeviceData deviceData);
    Task<bool> TryAutoReconnectAsync(PairedDevice device);
}
