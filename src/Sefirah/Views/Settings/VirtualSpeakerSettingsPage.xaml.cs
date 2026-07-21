using System.ComponentModel;
using System.Runtime.CompilerServices;
using NotifyRelay.Data.Contracts;
using NotifyRelay.Data.Items;
using NotifyRelay.DeviceCtrl.AudioRelay;
using NotifyRelay.Native;

namespace NotifyRelay.Views.Settings;

public sealed partial class VirtualSpeakerSettingsPage : Page
{
    public VirtualSpeakerViewModel ViewModel => (VirtualSpeakerViewModel)DataContext;

    public VirtualSpeakerSettingsPage()
    {
        InitializeComponent();
        Loaded += VirtualSpeakerSettingsPage_Loaded;
        SetupBreadcrumb();
    }

    private void VirtualSpeakerSettingsPage_Loaded(object sender, RoutedEventArgs e)
    {
        ViewModel.StatusChanged += OnStatusChanged;
        UpdateStatusUI();
    }

    private void SetupBreadcrumb()
    {
        BreadcrumbBar.ItemsSource = new ObservableCollection<BreadcrumbBarItemModel>
        {
            new("虚拟扬声器", typeof(VirtualSpeakerSettingsPage))
        };
        BreadcrumbBar.ItemClicked += BreadcrumbBar_ItemClicked;
    }

    private void BreadcrumbBar_ItemClicked(BreadcrumbBar sender, BreadcrumbBarItemClickedEventArgs args)
    {
        if (Frame.CanGoBack)
        {
            Frame.GoBack();
        }
    }

    private void OnStatusChanged(object? sender, EventArgs e)
    {
        UpdateStatusUI();
    }

    private void UpdateStatusUI()
    {
        ViewModel.OnPropertyChanged(nameof(ViewModel.IsActive));
        ViewModel.OnPropertyChanged(nameof(ViewModel.CanStart));
        ViewModel.OnPropertyChanged(nameof(ViewModel.StatusText));
    }

    private async void StartSendButton_Click(object sender, RoutedEventArgs e)
    {
        StartSendButton.IsEnabled = false;
        ViewModel.StatusText = "正在启动音频发送...";
        await ViewModel.StartSendAsync();
        UpdateStatusUI();
    }

    private async void StartRecvButton_Click(object sender, RoutedEventArgs e)
    {
        StartRecvButton.IsEnabled = false;
        ViewModel.StatusText = "正在请求远端音频...";
        await ViewModel.StartReceiveAsync();
        UpdateStatusUI();
    }

    private async void StopButton_Click(object sender, RoutedEventArgs e)
    {
        await ViewModel.StopAsync();
        UpdateStatusUI();
    }
}

public class VirtualSpeakerViewModel
{
    private readonly IGeneralSettingsService _generalSettingsService;
    private readonly AudioRelayService _audioRelayService;
    private readonly IDeviceManager _deviceManager;

    public event EventHandler? StatusChanged;

    private string _statusText = "状态：未启动";
    public string StatusText
    {
        get => _statusText;
        set
        {
            _statusText = value;
            OnPropertyChanged();
        }
    }

    public bool IsActive => _audioRelayService.IsActive;
    public bool CanStart => !IsActive && NativeCore.Context != IntPtr.Zero;

    public VirtualSpeakerViewModel()
    {
        _generalSettingsService = Ioc.Default.GetService<IGeneralSettingsService>()!;
        _audioRelayService = Ioc.Default.GetService<AudioRelayService>()!;
        _deviceManager = Ioc.Default.GetService<IDeviceManager>()!;

        _audioRelayService.StatusChanged += (s, e) =>
        {
            StatusChanged?.Invoke(this, EventArgs.Empty);
            StatusText = _audioRelayService.IsActive ? "状态：运行中" : "状态：未启动";
            OnPropertyChanged(nameof(IsActive));
            OnPropertyChanged(nameof(CanStart));
            OnPropertyChanged(nameof(StatusText));
        };
    }

    public async Task StartSendAsync()
    {
        var device = _deviceManager.ActiveDevice;
        if (device == null)
        {
            StatusText = "未选择目标设备";
            return;
        }
        await _audioRelayService.StartSendAsync(device.Id, device.RemoteIpAddress ?? device.IpAddresses?.FirstOrDefault() ?? "");
        StatusText = _audioRelayService.IsActive ? "状态：发送中" : "启动发送失败";
    }

    public async Task StartReceiveAsync()
    {
        var device = _deviceManager.ActiveDevice;
        if (device == null)
        {
            StatusText = "未选择目标设备";
            return;
        }
        await _audioRelayService.StartReceiveAsync(device.Id, device.RemoteIpAddress ?? device.IpAddresses?.FirstOrDefault() ?? "");
        StatusText = _audioRelayService.IsActive ? "状态：接收中" : "启动接收失败";
    }

    public async Task StopAsync()
    {
        await _audioRelayService.StopAsync();
        StatusText = "状态：未启动";
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
