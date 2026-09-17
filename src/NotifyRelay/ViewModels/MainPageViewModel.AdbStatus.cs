namespace NotifyRelay.ViewModels;

public sealed partial class MainPageViewModel
{
    #region Properties

    /// <summary>
    /// 当前设备是否正在运行仅音频模式的 scrcpy
    /// </summary>
    public bool IsAudioOnlyRunning
    {
        get
        {
            if (Device == null)
                return false;
            return ScreenMirrorService.IsAudioOnlyRunning(Device.Id);
        }
    }

    /// <summary>
    /// 获取当前音频状态的图标
    /// </summary>
    public string AudioStatusIcon
    {
        get
        {
            return IsAudioOnlyRunning ? "\uE995" : "\uE74F";
        }
    }

    /// <summary>
    /// 获取当前音频状态的文本描述
    /// </summary>
    public string AudioStatusText
    {
        get
        {
            return IsAudioOnlyRunning ? "仅音频播放中" : "未转发音频";
        }
    }

    /// <summary>
    /// 获取所有连接的ADB设备类型
    /// </summary>
    public List<string> AdbConnectionTypes
    {
        get
        {
            var connectionTypes = new List<string>();

            if (Device == null || !Device.HasAdbConnection || Device.ConnectedAdbDevices.Count == 0)
            {
                return connectionTypes;
            }

            // 检查所有连接的ADB设备，添加所有连接类型
            if (Device.ConnectedAdbDevices.Any(d => d.Type == NotifyRelay.Data.Enums.DeviceType.USB))
            {
                connectionTypes.Add("USB");
            }

            if (Device.ConnectedAdbDevices.Any(d => d.Type == NotifyRelay.Data.Enums.DeviceType.WIFI))
            {
                connectionTypes.Add("WiFi");
            }

            return connectionTypes;
        }
    }

    /// <summary>
    /// 获取ADB设备的详细信息，用于悬浮提示
    /// </summary>
    public string AdbDeviceInfo
    {
        get
        {
            var deviceInfo = new List<string>();

            // 设备名称
            if (!string.IsNullOrEmpty(Device?.Name))
            {
                deviceInfo.Add(Device.Name);
            }

            // 设备型号
            if (!string.IsNullOrEmpty(Device?.Model))
            {
                deviceInfo.Add(Device.Model);
            }

            // IP地址
            if (Device?.IpAddresses != null && Device.IpAddresses.Count > 0)
            {
                deviceInfo.Add(string.Join(", ", Device.IpAddresses));
            }

            // 确保至少返回一个默认值，便于调试
            if (deviceInfo.Count == 0)
            {
                deviceInfo.Add("设备信息不可用");
                if (Device == null)
                {
                    deviceInfo.Add("Device为null");
                }
                else if (Device.ConnectedAdbDevices.Count == 0)
                {
                    deviceInfo.Add("ConnectedAdbDevices为空");
                }
            }

            return string.Join("\n", deviceInfo);
        }
    }

    /// <summary>
    /// 获取所有连接的ADB设备图标
    /// </summary>
    public List<string> AdbStatusIcons
    {
        get
        {
            var icons = new List<string>();

            if (Device == null || !Device.HasAdbConnection || Device.ConnectedAdbDevices.Count == 0)
            {
                return icons;
            }

            // 添加USB图标
            if (Device.ConnectedAdbDevices.Any(d => d.Type == NotifyRelay.Data.Enums.DeviceType.USB))
            {
                icons.Add("\uE89E"); // USB图标
            }

            // 添加WiFi图标
            if (Device.ConnectedAdbDevices.Any(d => d.Type == NotifyRelay.Data.Enums.DeviceType.WIFI))
            {
                icons.Add("\uE927"); // WiFi图标
            }

            return icons;
        }
    }
    #endregion

    /// <summary>
    /// 手动刷新音频状态属性，用于UI更新
    /// </summary>
    public void RefreshAudioStatus()
    {
        OnPropertyChanged(nameof(IsAudioOnlyRunning));
        OnPropertyChanged(nameof(AudioStatusIcon));
        OnPropertyChanged(nameof(AudioStatusText));
    }

    /// <summary>
    /// ADB设备集合变化时的事件处理方法
    /// </summary>
    private void OnAdbDevicesCollectionChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
    {
        OnPropertyChanged(nameof(AdbConnectionTypes));
        OnPropertyChanged(nameof(AdbStatusIcons));
        OnPropertyChanged(nameof(AdbDeviceInfo));
    }
}
