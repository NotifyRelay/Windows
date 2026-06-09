using System.ComponentModel;
using System.Runtime.CompilerServices;
using NotifyRelay.Data.Contracts;
using NotifyRelay.Data.Items;
using NotifyRelay.DeviceCtrl.VirtualSpeaker;

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
        if (ViewModel.IsStreaming)
        {
            StatusTextBlock.Text = "状态：运行中";
            StatusTextBlock.Foreground = new SolidColorBrush(Microsoft.UI.Colors.Green);
        }
        else
        {
            StatusTextBlock.Text = "状态：未启动";
            StatusTextBlock.Foreground = new SolidColorBrush(Microsoft.UI.Colors.Gray);
        }
    }

    private async void ScanButton_Click(object sender, RoutedEventArgs e)
    {
        ScanButton.IsEnabled = false;
        ScanButton.Content = "扫描中...";
        ViewModel.DiscoveryStatus = "正在扫描DLNA设备...";

        await ViewModel.ScanDevicesAsync();

        ScanButton.Content = "开始扫描";
        ScanButton.IsEnabled = true;
    }
}

public class VirtualSpeakerViewModel
{
    private readonly IGeneralSettingsService _generalSettingsService;
    private readonly VirtualSpeakerService _virtualSpeakerService;

    public event EventHandler? StatusChanged;

    public ObservableCollection<DlnaRendererInfo> AvailableRenderers { get; } = new();

    private string _discoveryStatus = string.Empty;
    public string DiscoveryStatus
    {
        get => _discoveryStatus;
        set
        {
            _discoveryStatus = value;
            OnPropertyChanged();
        }
    }

    public string? SelectedDeviceId
    {
        get => _generalSettingsService.VirtualSpeakerTargetDeviceId;
        set
        {
            _generalSettingsService.VirtualSpeakerTargetDeviceId = value;
            if (value != null)
            {
                var device = AvailableRenderers.FirstOrDefault(r => r.Id == value);
                _generalSettingsService.VirtualSpeakerTargetDeviceName = device?.Name;
            }
        }
    }

    public bool IsStreaming => _virtualSpeakerService.IsRunning;

    public bool IsEnabled
    {
        get => _virtualSpeakerService.IsRunning;
        set
        {
            if (value && !_virtualSpeakerService.IsRunning)
            {
                _ = _virtualSpeakerService.StartStreaming();
            }
            else if (!value && _virtualSpeakerService.IsRunning)
            {
                _ = _virtualSpeakerService.StopStreaming();
            }
        }
    }

    public bool MuteOnStart
    {
        get => _generalSettingsService.VirtualSpeakerMuteOnStart;
        set => _generalSettingsService.VirtualSpeakerMuteOnStart = value;
    }

    public VirtualSpeakerViewModel()
    {
        _generalSettingsService = Ioc.Default.GetService<IGeneralSettingsService>()!;
        _virtualSpeakerService = Ioc.Default.GetService<VirtualSpeakerService>()!;
        _virtualSpeakerService.StatusChanged += (s, e) =>
        {
            StatusChanged?.Invoke(this, EventArgs.Empty);
            OnPropertyChanged(nameof(IsStreaming));
            OnPropertyChanged(nameof(IsEnabled));
        };
    }

    public async Task ScanDevicesAsync()
    {
        try
        {
            var renderers = await _virtualSpeakerService.DiscoverRenderersAsync();
            AvailableRenderers.Clear();
            foreach (var renderer in renderers)
            {
                AvailableRenderers.Add(renderer);
            }
            DiscoveryStatus = renderers.Any()
                ? $"发现 {renderers.Count} 个DLNA设备"
                : "未发现DLNA设备";
        }
        catch (Exception ex)
        {
            DiscoveryStatus = $"扫描失败: {ex.Message}";
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
