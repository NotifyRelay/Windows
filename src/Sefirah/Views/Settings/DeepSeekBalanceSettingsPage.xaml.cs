using NotifyRelay.Data.Contracts;
using NotifyRelay.Data.Items;
using NotifyRelay.DeviceCtrl.DeepSeekBalance;

namespace NotifyRelay.Views.Settings;

public sealed partial class DeepSeekBalanceSettingsPage : Page
{
    public DeepSeekBalanceViewModel ViewModel => (DeepSeekBalanceViewModel)DataContext;

    public DeepSeekBalanceSettingsPage()
    {
        InitializeComponent();
        Loaded += DeepSeekBalanceSettingsPage_Loaded;
        SetupBreadcrumb();
    }

    private void DeepSeekBalanceSettingsPage_Loaded(object sender, RoutedEventArgs e)
    {
        ViewModel.Initialize();
        UpdateStatusUI();
        ViewModel.StatusChanged += OnStatusChanged;
        ViewModel.HistoryChanged += OnHistoryChanged;
        ViewModel.BalanceUpdated += OnBalanceUpdated;
    }

    private void SetupBreadcrumb()
    {
        BreadcrumbBar.ItemsSource = new ObservableCollection<BreadcrumbBarItemModel>
        {
            new("DeepSeek余额", typeof(DeepSeekBalanceSettingsPage))
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

    private void OnHistoryChanged(object? sender, EventArgs e)
    {
    }

    private void OnBalanceUpdated(object? sender, BalanceHistoryItem e)
    {
        UpdateStatusUI();
    }

    private void UpdateStatusUI()
    {
        if (ViewModel.IsEnabled)
        {
            MonitorStatusTextBlock.Text = "状态：监控中";
            MonitorStatusTextBlock.Foreground = new SolidColorBrush(Microsoft.UI.Colors.Green);
        }
        else
        {
            MonitorStatusTextBlock.Text = "状态：已停止";
            MonitorStatusTextBlock.Foreground = new SolidColorBrush(Microsoft.UI.Colors.Gray);
        }
    }

    private void SaveToken_Click(object sender, RoutedEventArgs e)
    {
        ViewModel.SaveToken();
    }

    private async void FetchNow_Click(object sender, RoutedEventArgs e)
    {
        var balance = await ViewModel.FetchBalanceAsync();
        if (balance.HasValue)
        {
            UpdateStatusUI();
        }
    }

    private async void ClearHistory_Click(object sender, RoutedEventArgs e)
    {
        if (Frame == null) return;

        var dialog = new ContentDialog
        {
            Title = "清除历史",
            Content = "确定要清除所有历史余额记录吗？此操作不可恢复。",
            PrimaryButtonText = "确定",
            CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = this.XamlRoot
        };

        var result = await dialog.ShowAsync();
        if (result == ContentDialogResult.Primary)
        {
            ViewModel.ClearHistory();
        }
    }
}

public sealed partial class DeepSeekBalanceViewModel : ObservableObject
{
    private readonly IGeneralSettingsService _generalSettingsService;
    private readonly DeepSeekBalanceService _balanceService;

    public event EventHandler? StatusChanged;
    public event EventHandler? HistoryChanged;
    public event EventHandler<BalanceHistoryItem>? BalanceUpdated;

    public ObservableCollection<BalanceHistoryItem> BalanceHistory => _balanceService.BalanceHistory;

    [ObservableProperty]
    private string _apiToken = string.Empty;

    [ObservableProperty]
    private bool _isEnabled;

    [ObservableProperty]
    private string _currentBalanceText = "当前余额：-- CNY";

    [ObservableProperty]
    private string _statusText = "状态：已停止";

    [ObservableProperty]
    private string _historyCountText = "暂无历史数据";

    [ObservableProperty]
    private int _selectedIntervalIndex = 1;

    public DeepSeekBalanceViewModel()
    {
        _generalSettingsService = Ioc.Default.GetRequiredService<IGeneralSettingsService>();
        _balanceService = Ioc.Default.GetRequiredService<DeepSeekBalanceService>();
    }

    public void Initialize()
    {
        ApiToken = _generalSettingsService.DeepSeekApiToken ?? string.Empty;
        IsEnabled = _balanceService.IsPolling;
        SelectedIntervalIndex = GetIntervalIndex(_generalSettingsService.DeepSeekBalancePollingInterval);
        UpdateCurrentBalanceText();
        UpdateHistoryCountText();
    }

    private int GetIntervalIndex(int intervalMs)
    {
        return intervalMs switch
        {
            1000 => 0,
            60000 => 1,
            1800000 => 2,
            86400000 => 3,
            _ => 1
        };
    }

    private int GetIntervalMs(int index)
    {
        return index switch
        {
            0 => 1000,
            1 => 60000,
            2 => 1800000,
            3 => 86400000,
            _ => 60000
        };
    }

    partial void OnSelectedIntervalIndexChanged(int value)
    {
        _generalSettingsService.DeepSeekBalancePollingInterval = GetIntervalMs(value);
        if (IsEnabled)
        {
            StopPolling();
            StartPolling();
        }
    }

    public void SaveToken()
    {
        _generalSettingsService.DeepSeekApiToken = ApiToken;
    }

    public async Task<double?> FetchBalanceAsync()
    {
        var balance = await _balanceService.FetchBalanceAsync();
        if (balance.HasValue)
        {
            UpdateCurrentBalanceText();
        }
        return balance;
    }

    public void ClearHistory()
    {
        _balanceService.ClearHistory();
        UpdateHistoryCountText();
        HistoryChanged?.Invoke(this, EventArgs.Empty);
    }

    public void StartPolling()
    {
        _balanceService.StartPolling();
        IsEnabled = true;
        UpdateStatusText();
        StatusChanged?.Invoke(this, EventArgs.Empty);
    }

    public void StopPolling()
    {
        _balanceService.StopPolling();
        IsEnabled = false;
        UpdateStatusText();
        StatusChanged?.Invoke(this, EventArgs.Empty);
    }

    partial void OnIsEnabledChanged(bool value)
    {
        if (value && !_balanceService.IsPolling)
        {
            StartPolling();
        }
        else if (!value && _balanceService.IsPolling)
        {
            StopPolling();
        }
    }

    private void UpdateCurrentBalanceText()
    {
        var balance = _balanceService.CurrentBalance;
        CurrentBalanceText = $"当前余额：{balance:F2} CNY";
    }

    private void UpdateStatusText()
    {
        StatusText = IsEnabled ? "状态：监控中" : "状态：已停止";
    }

    private void UpdateHistoryCountText()
    {
        var count = BalanceHistory.Count;
        HistoryCountText = count > 0 ? $"共 {count} 条记录" : "暂无历史数据";
    }
}
