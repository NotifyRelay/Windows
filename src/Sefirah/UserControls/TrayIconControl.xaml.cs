using System.ComponentModel;
using CommunityToolkit.WinUI;
using Microsoft.UI;
using Microsoft.UI.Xaml.Media.Imaging;
using NotifyRelay.Data.Contracts;
using NotifyRelay.Data.Models;
using NotifyRelay.DeviceCtrl.AudioRelay;
using NotifyRelay.DeviceCtrl.DeepSeekBalance;
#if WINDOWS
using NotifyRelay.Platforms.Windows.Interop;
#endif
using Windows.UI.ViewManagement;

namespace NotifyRelay.UserControls;

public sealed partial class TrayIconControl : UserControl, INotifyPropertyChanged
{
    private readonly UISettings uiSettings = new();
    private IScreenMirrorService ScreenMirrorService { get; } = Ioc.Default.GetRequiredService<IScreenMirrorService>();
    private IDeviceManager DeviceManager { get; } = Ioc.Default.GetRequiredService<IDeviceManager>();
    private DeepSeekBalanceService DeepSeekBalanceService { get; } = Ioc.Default.GetRequiredService<DeepSeekBalanceService>();
    private IGeneralSettingsService GeneralSettingsService { get; } = Ioc.Default.GetRequiredService<IGeneralSettingsService>();
    private AudioRelayService AudioRelayService { get; } = Ioc.Default.GetRequiredService<AudioRelayService>();
    public PairedDevice? Device => DeviceManager.ActiveDevice;

    private bool _isAudioRelayActive;
    public bool IsAudioRelayActive
    {
        get => _isAudioRelayActive;
        set
        {
            if (_isAudioRelayActive != value)
            {
                _isAudioRelayActive = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsAudioRelayActive)));
            }
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public TrayIconControl()
    {
        InitializeComponent();

        AudioRelayService.StatusChanged += (s, e) =>
        {
            IsAudioRelayActive = AudioRelayService.IsActive;
        };

        UpdateTrayIcon(uiSettings);
        uiSettings.ColorValuesChanged += UpdateTrayIcon;

        DeepSeekBalanceService.BalanceUpdated += OnBalanceUpdated;
        UpdateTrayTooltip();
    }

    private void OnBalanceUpdated(object? sender, BalanceHistoryItem e)
    {
        UpdateTrayTooltip();
    }

    private void UpdateTrayTooltip()
    {
        try
        {
            var tooltip = "NotifyRelay";

            if (DeepSeekBalanceService.IsPolling && DeepSeekBalanceService.CurrentBalance > 0)
            {
                tooltip = $"NotifyRelay\nDeepSeek 余额: ¥{DeepSeekBalanceService.CurrentBalance:F2}";
            }

            _ = DispatcherQueue.EnqueueAsync(() => TrayIcon.ToolTipText = tooltip);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"更新托盘提示失败：{ex.Message}");
        }
    }

    [RelayCommand]
    public void ShowHideWindow()
    {
#if WINDOWS
        var window = App.MainWindow;
        if (window.Visible)
        {
            window.AppWindow.Hide();
        }
        else
        {
            window.AppWindow.Show();
            InteropHelpers.SetForegroundWindow(App.WindowHandle);
        }
#endif
    }

    [RelayCommand]
    public void StartScrcpy()
    {
        if (Device != null)
        {
            ScreenMirrorService.StartScrcpy(Device);
        }
    }

    private void UpdateTrayIcon(UISettings sender, object? args = null)
    {
        try
        {
            var iconPath = sender.GetColorValue(UIColorType.Background) == Colors.Black
                ? "ms-appx:///Assets/Icons/SefirahDark.ico"
                : "ms-appx:///Assets/Icons/SefirahLight.ico";

            _ = DispatcherQueue.EnqueueAsync(() => TrayIcon.IconSource = new BitmapImage(new(iconPath)));
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"检测主题失败：{ex.Message}");
        }
    }

    [RelayCommand]
    public async Task ToggleAudioRelay()
    {
        if (AudioRelayService.IsActive)
        {
            await AudioRelayService.StopAsync();
        }
        else
        {
            var device = DeviceManager.ActiveDevice;
            if (device != null)
            {
                await AudioRelayService.StartSendAsync(device.Id, device.RemoteIpAddress ?? "");
            }
        }
    }

    [RelayCommand]
    public void ExitApplication()
    {
        App.HandleClosedEvents = false;
        TrayIcon.Dispose();

        App.MainWindow?.Close();
        App.Current.Exit();

        Process.GetCurrentProcess().Kill();
    }
}
