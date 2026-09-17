using NotifyRelay.Data.Models;
using NotifyRelay.Utils;

#if WINDOWS
using NotifyRelay.Platforms.Windows.Services;
#endif

namespace NotifyRelay.ViewModels;

public sealed partial class MainPageViewModel
{
    #region Commands

    [RelayCommand]
    public async Task StartScrcpy()
    {
        try
        {
            LoadingScrcpy = true;
            await ScreenMirrorService.StartScrcpy(Device!);
        }
        finally
        {
            await Task.Delay(1000);
            LoadingScrcpy = false;
        }
    }

    [RelayCommand]
    public void SwitchToNextDevice(int delta)
    {
        if (PairedDevices.Count <= 1)
            return;

        var currentIndex = -1;
        for (int i = 0; i < PairedDevices.Count; i++)
        {
            if (PairedDevices[i].Id == Device?.Id)
            {
                currentIndex = i;
                break;
            }
        }

        if (currentIndex == -1)
            return;

        int nextIndex;
        if (delta < 0)
        {
            // Move to next device (or loop back to first)
            nextIndex = (currentIndex + 1) % PairedDevices.Count;
        }
        else
        {
            // Move to previous device (or loop to last)
            nextIndex = (currentIndex - 1 + PairedDevices.Count) % PairedDevices.Count;
        }

        DeviceManager.ActiveDevice = PairedDevices[nextIndex];
    }

    [RelayCommand]
    public async Task SetRingerMode(string? modeStr)
    {
        if (int.TryParse(modeStr, out int mode))
        {
            // 模式2：仅音频播放中，模式0：未转发音频
            if (mode == 2)
            {
                // 启动仅音频模式的 scrcpy
                await ScreenMirrorService.StartScrcpy(Device!, "--no-video");
            }
            else if (mode == 0)
            {
                // 停止 scrcpy 进程
                ScreenMirrorService.StopScrcpyByDeviceId(Device!.Id);
            }
            // 不再发送铃声模式消息到设备

            // 刷新音频状态属性，更新UI
            RefreshAudioStatus();
        }
    }

    [RelayCommand]
    public void ClearAllNotificationall()
    {
        NotificationService.ClearAllNotificationall();
    }

    [RelayCommand]
    public void Update()
    {
        UpdateService.DownloadUpdatesAsync();
    }

    [RelayCommand]
    public void RemoveNotification(Notification notification)
    {
        NotificationService.RemoveNotification(Device!, notification);
    }

    [RelayCommand]
    public void ClearAllNotifications(string appPackage)
    {
        NotificationService.ClearAllNotifications(appPackage);
    }

    [RelayCommand]
    public void SendMediaControl(string mediaControlParam)
    {
        if (string.IsNullOrEmpty(mediaControlParam))
        {
            return;
        }

        // 解析参数：格式为 "deviceId:action"
        var parts = mediaControlParam.Split(':');
        if (parts.Length != 2)
        {
            return;
        }

        string deviceId = parts[0];
        string action = parts[1];

        // 发送媒体控制请求到指定设备
        PlaybackService.SendMediaControlRequest(deviceId, action);
    }

    [RelayCommand]
    public void StartftpConnection()
    {
        if (Device != null)
        {
            // 设置手动发送过ftp请求的标记
            Device.HasSentftpRequest = true;
#if WINDOWS
            NetworkDriveMapper.SendftpCommand(Device, "start");
#endif
        }
    }

    #endregion

    #region Methods

    public async Task OpenApp(Notification notification, string? deviceId = null)
    {
        ArgumentNullException.ThrowIfNull(notification);
        Debug.WriteLine($"[调试] MainPageViewModel.OpenApp 被调用：notification.Key={notification.Key} deviceId={deviceId}");

        // 如果未指定设备ID，使用当前活跃设备
        var targetDevice = deviceId != null ? DeviceManager.FindDeviceById(deviceId) : Device;
        if (targetDevice == null)
        {
            Debug.WriteLine("[警告] 找不到目标设备（targetDevice 为 null），取消打开应用。请检查 deviceId 是否正确或设备是否已配对。");
            return;
        }

        var rawJson = JsonSerializer.Serialize(new
        {
            type = "DATA_NOTIFICATION",
            notificationType = "Invoke",
            notificationKey = notification.Key ?? string.Empty,
        });
        var notificationJson = rawJson;
        if (notificationJson == null) return;

        string? appIcon = string.Empty;
        if (!string.IsNullOrEmpty(notification.AppPackage))
        {
            appIcon = IconUtils.GetAppIconFilePath(notification.AppPackage);
        }

        Debug.WriteLine($"[调试] 调用 ScreenMirrorService.StartScrcpy: deviceId={targetDevice.Id} appPackage={notification.AppPackage} appIcon={appIcon}");
        var started = await ScreenMirrorService.StartScrcpy(targetDevice, $"--new-display --start-app={notification.AppPackage}", appIcon);

        Debug.WriteLine($"[调试] ScreenMirrorService.StartScrcpy 返回: started={started}");

        if (started && targetDevice.ConnectionStatus)
        {
            Debug.WriteLine($"[调试] scrcpy 已启动且设备连接，等待 2s 然后发送通知调用到设备 {targetDevice.Id}");
            await Task.Delay(2000);
            SessionManager.SendMessage(targetDevice.Id, notificationJson);
        }
    }

    public void ToggleNotificationPin(Notification notification)
    {
        if (Device != null)
        {
            NotificationService.TogglePinNotification(Device, notification);
        }
    }

    public void SendFiles(IReadOnlyList<IStorageItem> storageItems)
    {
        FileTransferService.SendFiles(storageItems);
    }

    #endregion
}
