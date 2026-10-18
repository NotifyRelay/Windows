using System.Runtime.InteropServices;
using CommunityToolkit.WinUI;
using Microsoft.UI.Dispatching;
using NAudio.CoreAudioApi;
using NAudio.Utils;
using NotifyRelay.Data.Models;
using NotifyRelay.Platforms.Windows.Interop;
using Windows.Devices.Enumeration;
using Windows.Media.Devices;

namespace NotifyRelay.Platforms.Windows.Services;

/// <summary>
/// 音频设备枚举、监听与音量/静音/默认设备管理。
/// 由 WindowsPlaybackService 的初始化流程启动，并由 MediaControlExecutor 处理远端音频控制指令。
/// </summary>
public class AudioDeviceManager(ILogger<AudioDeviceManager> logger) : IDisposable
{
    private readonly Microsoft.UI.Dispatching.DispatcherQueue dispatcher = Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread();
    private readonly List<AudioDevice> audioDevices = [];
    private readonly MMDeviceEnumerator enumerator = new();

    // WinRT device watcher for audio endpoint changes
    private DeviceWatcher? deviceWatcher;

    /// <summary>
    /// 创建并启动音频设备监视器。失败时不阻断调用方初始化流程。
    /// </summary>
    public void StartWatcher()
    {
        // Use WinRT DeviceWatcher to monitor audio device add/remove/update events.
        try
        {
            deviceWatcher = DeviceInformation.CreateWatcher(MediaDevice.GetAudioRenderSelector());
            deviceWatcher.Added += DeviceWatcher_Added;
            deviceWatcher.Removed += DeviceWatcher_Removed;
            deviceWatcher.Updated += DeviceWatcher_Updated;
            deviceWatcher.EnumerationCompleted += DeviceWatcher_EnumerationCompleted;
            deviceWatcher.Start();

            // Subscribe to default audio render device changes and update selection accordingly.
            MediaDevice.DefaultAudioRenderDeviceChanged += MediaDevice_DefaultAudioRenderDeviceChanged;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "无法启动设备监视器，回退到手动/定期刷新");
        }
    }

    public void GetAllAudioDevices()
    {
        try
        {
            audioDevices.Clear();

            // Get the default device ID (NAudio)
            string defaultDeviceId;
            using (var defaultDevice = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia))
            {
                defaultDeviceId = defaultDevice.ID;
            }

            // List all active devices（MMDevice 与集合均为 IDisposable，需确定性释放）
            using var devices = enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active);
            foreach (var device in devices)
            {
                using (device)
                {
                    audioDevices.Add(
                        new AudioDevice
                        {
                            DeviceId = device.ID,
                            DeviceName = device.FriendlyName,
                            Volume = device.AudioEndpointVolume.MasterVolumeLevelScalar,
                            IsMuted = device.AudioEndpointVolume.Mute,
                            IsSelected = device.ID == defaultDeviceId
                        }
                    );
                }
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "枚举音频设备失败");
        }
    }

    /// <summary>切换指定设备的静音状态。</summary>
    /// <returns>实际执行成功返回 true；设备不存在、未激活或调用失败返回 false。</returns>
    public bool ToggleMute(string deviceId)
    {
        try
        {
            using var endpoint = enumerator.GetDevice(deviceId);
            if (endpoint is null || endpoint.State != DeviceState.Active)
            {
                logger.LogWarning("设备 {DeviceId} 不存在或未激活，无法切换静音", deviceId);
                return false;
            }

            try
            {
                endpoint.AudioEndpointVolume.Mute = !endpoint.AudioEndpointVolume.Mute;
                return true;
            }
            catch (COMException comEx) when (comEx.HResult == unchecked((int)0x8007001F))
            {
                logger.LogWarning("设备 {DeviceId} 在静音时无法正常工作", deviceId);
                return false;
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "静音设备 {DeviceId} 时出错", deviceId);
            return false;
        }
    }

    /// <summary>设置指定设备的音量。</summary>
    /// <returns>实际执行成功返回 true；设备不存在、未激活或调用失败返回 false。</returns>
    public bool SetVolume(string deviceId, float volume)
    {
        try
        {
            using var endpoint = enumerator.GetDevice(deviceId);
            if (endpoint is null || endpoint.State is not DeviceState.Active)
            {
                logger.LogWarning("设备 {DeviceId} 不存在或未激活，无法设置音量", deviceId);
                return false;
            }

            try
            {
                endpoint.AudioEndpointVolume.MasterVolumeLevelScalar = volume;
                return true;
            }
            catch (COMException comEx) when (comEx.HResult == unchecked((int)0x8007001F))
            {
                logger.LogWarning("设备 {DeviceId} 在设置音量时无法正常工作", deviceId);
                return false;
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "为设备 {DeviceId} 设置音量 {Volume} 时出错", volume, deviceId);
            return false;
        }
    }


    /// <summary>设置系统默认音频设备。</summary>
    /// <returns>实际执行成功返回 true；COM 创建失败、SetDefaultEndpoint 返回非 S_OK 或异常时返回 false。</returns>
    public bool SetDefaultAudioDevice(string deviceId)
    {
        object? policyConfigObject = null;
        try
        {
            Type? policyConfigType = Type.GetTypeFromCLSID(new Guid("870af99c-171d-4f9e-af0d-e63df40c2bc9"));
            if (policyConfigType is null)
            {
                logger.LogError("无法解析 IPolicyConfig 的 COM 类型");
                return false;
            }

            policyConfigObject = Activator.CreateInstance(policyConfigType);
            if (policyConfigObject is null)
            {
                logger.LogError("无法创建 IPolicyConfig COM 实例");
                return false;
            }

            if (policyConfigObject is not IPolicyConfig policyConfig)
            {
                logger.LogError("IPolicyConfig COM 实例类型不匹配");
                return false;
            }

            int result1 = policyConfig.SetDefaultEndpoint(deviceId, ERole.eMultimedia);
            int result2 = policyConfig.SetDefaultEndpoint(deviceId, ERole.eCommunications);
            int result3 = policyConfig.SetDefaultEndpoint(deviceId, ERole.eConsole);

            if (result1 != HResult.S_OK || result2 != HResult.S_OK || result3 != HResult.S_OK)
            {
                logger.LogError("SetDefaultEndpoint 返回错误代码：{Result1}, {Result2}, {Result3}", result1, result2, result3);
                return false;
            }

            var index = audioDevices.FindIndex(d => d.DeviceId == deviceId);

            if (index != -1)
            {
                audioDevices.First().IsSelected = false;
                audioDevices[index].IsSelected = true;
            }

            return true;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "设置默认设备时出错");
            return false;
        }
        finally
        {
            if (policyConfigObject is not null)
            {
                Marshal.ReleaseComObject(policyConfigObject);
            }
        }
    }

    // WinRT DeviceWatcher / MediaDevice 事件处理，替代 IMMNotificationClient 回调
    private void DeviceWatcher_Added(DeviceWatcher sender, DeviceInformation args)
    {
        _ = dispatcher.EnqueueAsync(() =>
        {
            try
            {
                GetAllAudioDevices();
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "处理设备添加事件时出错");
            }
        });
    }

    private void DeviceWatcher_Removed(DeviceWatcher sender, DeviceInformationUpdate args)
    {
        _ = dispatcher.EnqueueAsync(() =>
        {
            try
            {
                GetAllAudioDevices();
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "处理设备移除事件时出错");
            }
        });
    }

    private void DeviceWatcher_Updated(DeviceWatcher sender, DeviceInformationUpdate args)
    {
        _ = dispatcher.EnqueueAsync(() =>
        {
            try
            {
                GetAllAudioDevices();
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "处理设备更新事件时出错");
            }
        });
    }

    private void DeviceWatcher_EnumerationCompleted(DeviceWatcher sender, object args)
    {
        _ = dispatcher.EnqueueAsync(() => { logger.LogDebug("设备枚举完成"); });
    }

    private void MediaDevice_DefaultAudioRenderDeviceChanged(object sender, DefaultAudioRenderDeviceChangedEventArgs args)
    {
        _ = dispatcher.EnqueueAsync(() =>
        {
            try
            {
                UpdateDefaultSelection();
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "处理默认设备更改时出错");
            }
        });
    }

    private void UpdateDefaultSelection()
    {
        try
        {
            using var defaultDevice = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
            if (defaultDevice != null)
            {
                var id = defaultDevice.ID;
                var index = audioDevices.FindIndex(d => d.DeviceId == id);
                if (index != -1)
                {
                    var selectedIndex = audioDevices.FindIndex(d => d.IsSelected == true);
                    if (selectedIndex != -1)
                        audioDevices[selectedIndex].IsSelected = false;
                    audioDevices[index].IsSelected = true;
                    logger.LogInformation("默认设备已更改：{DefaultDeviceId}", id);
                }
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "更新默认音频设备选择时出错");
        }
    }

    /// <summary>释放长期持有的 COM 资源（设备枚举器）。</summary>
    public void Dispose()
    {
        enumerator.Dispose();
    }
}
