using CommunityToolkit.WinUI;
using NotifyRelay.Data.Contracts;
using NotifyRelay.Data.Enums;
using NotifyRelay.Data.Models;
using NotifyRelay.Extensions;
using NotifyRelay.Utils;

namespace NotifyRelay.Services.Media;

/// <summary>
/// scrcpy 设备选择阶段：匹配 ADB 设备 → 按偏好/弹窗选定 serial →
/// 必要时解锁设备 → 必要时自动重连并重新匹配。
/// </summary>
/// <remarks>
/// 承接原 <c>ScreenMirrorService</c> 中的 <c>ResolveDeviceSerialAsync</c> 与
/// <c>ShowDeviceSelectionDialog</c>，方法体逐字搬移。
///
/// <c>devices</c> 集合仍指向 <c>adbService.AdbDevices</c> 同一实例（由构造时捕获），
/// 因此集合变更的可见性与拆分前一致。
/// </remarks>
internal sealed class ScrcpyDeviceSelector(
    ILogger<ScreenMirrorService> logger,
    IAdbService adbService,
    ObservableCollection<AdbDevice> devices,
    ScrcpyPasswordCache passwordCacheStore,
    Microsoft.UI.Dispatching.DispatcherQueue? dispatcher)
{
    /// <summary>
    /// 设备选择阶段（原 StartScrcpy 主体）：匹配 ADB 设备 → 按偏好/弹窗选定 serial →
    /// 必要时解锁设备 → 必要时自动重连并重新匹配。
    /// 返回 null 表示应中止启动（调用方返回 false），等价于拆分前各处的 `return false`。
    /// </summary>
    public async Task<string?> ResolveDeviceSerialAsync(
        PairedDevice device,
        IDeviceSettingsService deviceSettings,
        List<string> argBuilder)
    {
        var devicePreferenceType = deviceSettings.ScrcpyDevicePreference;
        string? selectedDeviceSerial = null;

        // 根据已配对设备信息优先匹配 ADB 设备：
        // 1. 优先匹配 AndroidId == PairedDevice.Id（绑定映射）
        // 2. 否则通过型号匹配
        // 3. 在候选中优先选择 USB（有线）设备
        var adbOnlineDevices = devices.Where(d => d != null && d.IsOnline).ToList();
        var matchedDevices = adbOnlineDevices.Where(d => !string.IsNullOrEmpty(d.AndroidId) && d.AndroidId == device.Id).ToList();

        if (matchedDevices.Count == 0 && !string.IsNullOrEmpty(device.Model))
        {
            matchedDevices = adbOnlineDevices.Where(d => string.IsNullOrEmpty(d.AndroidId) &&
                !string.IsNullOrEmpty(d.Model) &&
                (device.Model.Equals(d.Model, StringComparison.OrdinalIgnoreCase) ||
                 device.Model.Contains(d.Model, StringComparison.OrdinalIgnoreCase) ||
                 d.Model.Contains(device.Model, StringComparison.OrdinalIgnoreCase)))
                .ToList();
        }

        var pairedDevices = matchedDevices.Count > 0 ? matchedDevices : devices.Where(d => d != null && d.Model == device.Model).ToList();

        // 如果存在多个候选 ADB 设备，弹窗选择并预选最优（USB 优先），而不是直接选择第一个
        if (pairedDevices.Count > 1)
        {
            var preferredSerial = pairedDevices.FirstOrDefault(d => d.Type == DeviceType.USB)?.Serial
                                  ?? pairedDevices.First().Serial;
            selectedDeviceSerial = await ShowDeviceSelectionDialog(pairedDevices, preferredSerial);
            if (string.IsNullOrEmpty(selectedDeviceSerial))
            {
                logger.LogWarning("用户在设备选择弹窗中取消或未选择设备");
                return null;
            }
        }
        else if (pairedDevices.Count > 0)
        {
            switch (devicePreferenceType)
            {
                case ScrcpyDevicePreferenceType.Usb:
                    // 优先选择已匹配设备中的 USB，否则从匹配列表中选择第一个
                    selectedDeviceSerial = pairedDevices.FirstOrDefault(d => d.Type == DeviceType.USB)?.Serial
                                        ?? pairedDevices.FirstOrDefault()?.Serial;
                    break;
                case ScrcpyDevicePreferenceType.Tcpip:
                    selectedDeviceSerial = pairedDevices.FirstOrDefault(d => d.Type == DeviceType.WIFI)?.Serial
                                        ?? pairedDevices.FirstOrDefault()?.Serial;
                    break;
                case ScrcpyDevicePreferenceType.Auto:
                    // 优先选择 USB，如果找到了 USB 且启用了 ADB TCP/IP 模式，则追加参数
                    var usbDevice = pairedDevices.FirstOrDefault(d => d.Type == DeviceType.USB);
                    if (usbDevice != null)
                    {
                        if (deviceSettings.AdbTcpipModeEnabled)
                        {
                            argBuilder.Add("--tcpip");
                        }
                        selectedDeviceSerial = usbDevice.Serial;
                    }
                    else
                    {
                        selectedDeviceSerial = pairedDevices.FirstOrDefault(d => d.Type == DeviceType.WIFI)?.Serial
                                            ?? pairedDevices.FirstOrDefault()?.Serial;
                    }
                    break;
                case ScrcpyDevicePreferenceType.AskEverytime:
                    // 计算首选序列号：优先 USB 设备
                    var preferred = pairedDevices.FirstOrDefault(d => d.Type == DeviceType.USB)?.Serial
                                    ?? pairedDevices.FirstOrDefault()?.Serial;
                    selectedDeviceSerial = await ShowDeviceSelectionDialog(pairedDevices, preferred);
                    if (string.IsNullOrEmpty(selectedDeviceSerial))
                    {
                        logger.LogWarning("未选择用于 scrcpy 的设备");
                        return null;
                    }
                    break;
            }
            var commands = deviceSettings.UnlockCommands?.Trim()
                .Split(['\n', '\r'], StringSplitOptions.RemoveEmptyEntries)
                .Select(c => c.Trim())
                .Where(c => !string.IsNullOrEmpty(c))
                .ToList();
            var adbDevice = pairedDevices.FirstOrDefault(d => d.Serial == selectedDeviceSerial);
            if (adbDevice is null || adbDevice.DeviceData == null) return null;

            if (commands?.Count > 0 && await adbService.IsLocked(adbDevice.DeviceData))
            {
                // Check if any command contains password placeholder
                var hasPasswordPlaceholder = commands.Any(c => c.Contains("%pwd%"));
                string? password = null;

                if (hasPasswordPlaceholder)
                {
                    // Only use password caching if timeout is greater than 0
                    var timeoutSeconds = deviceSettings.UnlockTimeout;
                    if (timeoutSeconds > 0)
                    {
                        // Try to get cached password first
                        password = passwordCacheStore.GetCachedPassword(device.Id, timeoutSeconds);
                    }

                    // If no cached password or caching is disabled, ask user for password
                    if (password is null)
                    {
                        password = await passwordCacheStore.ShowPasswordInputDialog();
                        if (password is null) return null;

                        // Only cache the password if timeout is greater than 0
                        if (timeoutSeconds > 0)
                        {
                            passwordCacheStore.CachePassword(device.Id, password, timeoutSeconds);
                        }
                    }

                    // Replace password placeholders with actual password
                    commands = commands.Select(c => c.Replace("%pwd%", password)).ToList();
                }

                adbService.UnlockDevice(adbDevice.DeviceData, commands);
            }
        }
        else if (deviceSettings.AdbTcpipModeEnabled && device.Session != null)
        {
            var connectedSessionIpAddress = device.Session.Socket.RemoteEndPoint?.ToString()?.Split(':')[0];
            if (await adbService.ConnectWireless(connectedSessionIpAddress))
            {
                selectedDeviceSerial = $"{connectedSessionIpAddress}:5555";
            }
        }
        else
        {
            logger.LogWarning("未找到匹配的ADB设备，尝试自动重连");

            // 尝试自动重连到设备的5555端口
            var reconnected = await adbService.TryAutoReconnectAsync(device);

            if (reconnected)
            {
                // 等待设备列表更新
                await Task.Delay(500);

                // 重新尝试匹配ADB设备
                var reconnectedAdbDevices = devices.Where(d => d.IsOnline).ToList();
                var rematchedDevices = reconnectedAdbDevices.Where(d => !string.IsNullOrEmpty(d.AndroidId) && d.AndroidId == device.Id).ToList();

                if (rematchedDevices.Count == 0 && !string.IsNullOrEmpty(device.Model))
                {
                    rematchedDevices = reconnectedAdbDevices.Where(d => string.IsNullOrEmpty(d.AndroidId) &&
                        !string.IsNullOrEmpty(d.Model) &&
                        (device.Model.Equals(d.Model, StringComparison.OrdinalIgnoreCase) ||
                         device.Model.Contains(d.Model, StringComparison.OrdinalIgnoreCase) ||
                         d.Model.Contains(device.Model, StringComparison.OrdinalIgnoreCase)))
                        .ToList();
                }

                if (rematchedDevices.Count > 0)
                {
                    var preferred = rematchedDevices.FirstOrDefault(d => d.Type == DeviceType.USB)?.Serial
                                    ?? rematchedDevices.FirstOrDefault()?.Serial;
                    selectedDeviceSerial = await ShowDeviceSelectionDialog(rematchedDevices, preferred);
                }
                else
                {
                    logger.LogWarning("自动重连后仍未找到匹配的ADB设备");
                    _ = dispatcher?.EnqueueAsync(async () =>
                    {
                        var dialog = new ContentDialog
                        {
                            XamlRoot = App.MainWindow.Content!.XamlRoot,
                            Title = "AdbDeviceOffline".GetLocalizedResource(),
                            Content = "AdbDeviceOfflineDescription".GetLocalizedResource(),
                            CloseButtonText = "Dismiss".GetLocalizedResource()
                        };
                        await dialog.ShowAsync();
                    });
                    return null;
                }
            }
            else
            {
                _ = dispatcher?.EnqueueAsync(async () =>
                {
                    var dialog = new ContentDialog
                    {
                        XamlRoot = App.MainWindow.Content!.XamlRoot,
                        Title = "AdbDeviceOffline".GetLocalizedResource(),
                        Content = "AdbDeviceOfflineDescription".GetLocalizedResource(),
                        CloseButtonText = "Dismiss".GetLocalizedResource()
                    };
                    await dialog.ShowAsync();
                });
                return null;
            }
        }

        return selectedDeviceSerial;
    }

    private async Task<string?> ShowDeviceSelectionDialog(List<AdbDevice> onlineDevices, string? preferredSerial = null)
    {
        string? selectedDeviceSerial = null;

        await dispatcher!.EnqueueAsync(async () =>
        {
            var deviceOptions = new List<ComboBoxItem>();
            foreach (var device in onlineDevices)
            {
                var displayName = device.Model ?? "Unknown";
                var item = new ComboBoxItem
                {
                    Content = $"{displayName} - {device.Type} ({device.Serial})",
                    Tag = device.Serial
                };
                deviceOptions.Add(item);
            }

            var deviceSelector = new ComboBox
            {
                ItemsSource = deviceOptions,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                SelectedIndex = 0
            };

            // 如果提供了首选序列号，尝试设置为选中项
            if (!string.IsNullOrEmpty(preferredSerial))
            {
                for (int i = 0; i < deviceOptions.Count; i++)
                {
                    if ((deviceOptions[i].Tag as string) == preferredSerial)
                    {
                        deviceSelector.SelectedIndex = i;
                        logger.LogDebug("[调试] 在设备选择弹窗中预选设备：{Serial}", preferredSerial);
                        break;
                    }
                }
            }

            var dialog = new ContentDialog
            {
                XamlRoot = App.MainWindow.Content!.XamlRoot,
                Title = "SelectDevice".GetLocalizedResource(),
                Content = deviceSelector,
                PrimaryButtonText = "Start".GetLocalizedResource(),
                CloseButtonText = "Cancel".GetLocalizedResource(),
                DefaultButton = ContentDialogButton.Primary
            };

            var result = await dialog.ShowAsync();

            if (result is ContentDialogResult.Primary && deviceSelector.SelectedItem is ComboBoxItem selected)
            {
                selectedDeviceSerial = selected.Tag as string;
            }
        });

        return selectedDeviceSerial;
    }
}
