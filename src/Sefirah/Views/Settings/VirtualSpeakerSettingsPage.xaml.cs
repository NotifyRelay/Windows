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
        ViewModel.DiscoveryStatus = "正在扫描SoundSeeder设备...";

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

    public ObservableCollection<SoundSeederDeviceInfo> AvailableSpeakers { get; } = new();

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
                var device = AvailableSpeakers.FirstOrDefault(r => r.Uuid == value);
                _generalSettingsService.VirtualSpeakerTargetDeviceName = device?.Name;
                _generalSettingsService.VirtualSpeakerTargetDeviceIp = device?.IpAddress;
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
                _ = _virtualSpeakerService.StartStreaming().ContinueWith(_ =>
                {
                    OnPropertyChanged(nameof(IsEnabled));
                }, TaskScheduler.FromCurrentSynchronizationContext());
            }
            else if (!value && _virtualSpeakerService.IsRunning)
            {
                _ = _virtualSpeakerService.StopStreamingAsync().ContinueWith(_ =>
                {
                    OnPropertyChanged(nameof(IsEnabled));
                }, TaskScheduler.FromCurrentSynchronizationContext());
            }
            else
            {
                OnPropertyChanged(nameof(IsEnabled));
            }
        }
    }

    public bool MuteOnStart
    {
        get => _generalSettingsService.VirtualSpeakerMuteOnStart;
        set => _generalSettingsService.VirtualSpeakerMuteOnStart = value;
    }

    public int SelectedStrategyIndex
    {
        get => _generalSettingsService.VirtualSpeakerStreamingStrategy;
        set
        {
            _generalSettingsService.VirtualSpeakerStreamingStrategy = value;
            _virtualSpeakerService.Strategy = (StreamingStrategy)value;
            OnPropertyChanged();
        }
    }

    public VirtualSpeakerViewModel()
    {
        _generalSettingsService = Ioc.Default.GetService<IGeneralSettingsService>()!;
        _virtualSpeakerService = Ioc.Default.GetService<VirtualSpeakerService>()!;
        _virtualSpeakerService.Strategy = (StreamingStrategy)_generalSettingsService.VirtualSpeakerStreamingStrategy;
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
            var speakers = await _virtualSpeakerService.DiscoverSpeakersAsync();
            AvailableSpeakers.Clear();
            foreach (var speaker in speakers)
            {
                AvailableSpeakers.Add(speaker);
            }
            DiscoveryStatus = speakers.Any()
                ? $"发现 {speakers.Count} 个SoundSeeder设备"
                : "未发现SoundSeeder设备";
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
