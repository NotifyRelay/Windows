using System.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using NotifyRelay.Data.Contracts;
using NotifyRelay.Data.Models;
using NotifyRelay.Dialogs;
using NotifyRelay.Native;
using NotifyRelay.Services.Infrastructure;
using NotifyRelay.Services.Protocol;

namespace NotifyRelay.ViewModels.Settings;

public partial class DevicesViewModel : ObservableObject
{
    #region Services
    private IDiscoveryService DiscoveryService { get; } = Ioc.Default.GetRequiredService<IDiscoveryService>();
    private IDeviceManager DeviceManager { get; } = Ioc.Default.GetRequiredService<IDeviceManager>();
    private PairingInitiator PairingInitiator { get; } = Ioc.Default.GetRequiredService<PairingInitiator>();
    #endregion

    public ObservableCollection<PairedDevice> PairedDevices => DeviceManager.PairedDevices;

    public ObservableCollection<DiscoveredDevice> DiscoveredDevices => DiscoveryService.DiscoveredDevices;

    public DevicesViewModel()
    {
        // DiscoveryService 每轮刷新整体替换 DiscoveredDevices 实例（规避 ItemsRepeater 崩溃），
        // 此处转发通知，使 x:Bind OneWay 拿到新实例
        DiscoveryService.PropertyChanged += OnDiscoveryServicePropertyChanged;
    }

    private void OnDiscoveryServicePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(IDiscoveryService.DiscoveredDevices))
        {
            OnPropertyChanged(nameof(DiscoveredDevices));
        }
    }

    /// <summary>
    /// 由本机发起配对（PC 作为发起端）。
    ///
    /// 展示本机生成的 6 位配对码，由对端设备输入；core 完成 SPAKE2 协商后经 RESULT 回调闭环，
    /// 故对话框保持打开直到结果返回或用户取消。
    /// </summary>
    public async Task PairDeviceAsync(DiscoveredDevice? device)
    {
        if (device == null) return;

        var root = App.MainWindow?.Content?.XamlRoot;
        if (root == null) return;

        if (string.IsNullOrEmpty(device.Ip))
        {
            await DialogHelper.ShowInfoAsync(root, "无法获取设备 IP", "请等待该设备重新出现在可配对列表中再试。");
            return;
        }

        var (code, completion) = await PairingInitiator.BeginAsync(
            device.DeviceId,
            SignedLocalBattery(),
            "pc");

        if (string.IsNullOrEmpty(code))
        {
            await DialogHelper.ShowInfoAsync(root, "配对初始化失败", "核心未就绪或无法解析对端地址。");
            return;
        }

        var dialog = new PairingCodeDisplayDialog(device.DeviceName, code)
        {
            XamlRoot = root,
        };

        // 结果到达（成功/失败/超时）即自动关闭对话框，用户无需点「完成」等待
        var closedByResult = false;
        _ = completion.ContinueWith(
            _ =>
            {
                closedByResult = true;
                App.MainWindow?.DispatcherQueue.TryEnqueue(() => dialog.Hide());
            },
            TaskScheduler.Default);

        var result = await dialog.ShowAsync();

        // 用户手动关闭（点取消/完成）且结果尚未返回：中止发起流程
        if (!closedByResult && !completion.IsCompleted)
        {
            PairingInitiator.Cancel(device.DeviceId);
            return;
        }

        var success = await completion;
        if (success)
        {
            await DialogHelper.ShowInfoAsync(root, "配对成功", $"{device.DeviceName} 已完成配对。");
            return;
        }

        var reason = PairingInitiator.FailureReason(device.DeviceId);
        await DialogHelper.ShowInfoAsync(
            root,
            "配对失败",
            PairingInitiator.Describe(reason, "对方拒绝、配对码错误或等待超时。"));
    }

    /// <summary>发起配对时报本机真实带符号电量（正=充电，负=放电）。</summary>
    private static int SignedLocalBattery()
    {
        var info = Ioc.Default.GetService<ISystemInfoService>();
        if (info == null) return -101;
        var level = info.GetSystemBatteryLevel();
        return info.GetSystemChargingStatus() ? Math.Abs(level) : -Math.Abs(level);
    }
}
