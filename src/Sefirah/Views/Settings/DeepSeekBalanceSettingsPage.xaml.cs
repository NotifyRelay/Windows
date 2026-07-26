using NotifyRelay.Data.Contracts;
using NotifyRelay.Data.Items;
using NotifyRelay.Worker.Services;

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
            Frame.GoBack();
    }

    private void OnStatusChanged(object? sender, EventArgs e) => UpdateStatusUI();
    private void OnHistoryChanged(object? sender, EventArgs e) { }
    private void OnBalanceUpdated(object? sender, BalanceHistoryItem e) => UpdateStatusUI();

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

    private void SaveToken_Click(object sender, RoutedEventArgs e) => ViewModel.SaveToken();

    private async void FetchNow_Click(object sender, RoutedEventArgs e)
    {
        var balance = await ViewModel.FetchBalanceAsync();
        if (balance.HasValue) UpdateStatusUI();
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
            ViewModel.ClearHistory();
    }
}

public sealed partial class DeepSeekBalanceViewModel : ObservableObject
{
    private readonly IGeneralSettingsService _generalSettingsService;
    private readonly DeepSeekBalanceService _deepSeekService;

    public event EventHandler? StatusChanged;
    public event EventHandler? HistoryChanged;
    public event EventHandler<BalanceHistoryItem>? BalanceUpdated;

    public ObservableCollection<BalanceHistoryItem> BalanceHistory { get; } = new();
    public ObservableCollection<BalanceHistoryItem> DisplayHistory { get; } = new();

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

    [ObservableProperty]
    private bool _isCollapsed;

    public DeepSeekBalanceViewModel()
    {
        _generalSettingsService = Ioc.Default.GetRequiredService<IGeneralSettingsService>();
        _deepSeekService = Ioc.Default.GetRequiredService<DeepSeekBalanceService>();

        _deepSeekService.StatusChanged += OnServiceStatusChanged;
        _deepSeekService.BalanceUpdated += OnServiceBalanceUpdated;
    }

    public void Initialize()
    {
        ApiToken = _generalSettingsService.DeepSeekApiToken ?? string.Empty;
        SelectedIntervalIndex = GetIntervalIndex(_generalSettingsService.DeepSeekBalancePollingInterval);
        IsCollapsed = _generalSettingsService.DeepSeekBalanceHistoryCollapsed;
        // 回显已按设置自动启动的服务实际运行状态
        IsEnabled = _deepSeekService.IsPolling;
    }

    private int GetIntervalIndex(int intervalMs) => intervalMs switch
    {
        1000 => 0, 60000 => 1, 1800000 => 2, 86400000 => 3, _ => 1
    };

    private int GetIntervalMs(int index) => index switch
    {
        0 => 1000, 1 => 60000, 2 => 1800000, 3 => 86400000, _ => 60000
    };

    partial void OnSelectedIntervalIndexChanged(int value)
    {
        _generalSettingsService.DeepSeekBalancePollingInterval = GetIntervalMs(value);
        if (IsEnabled) TogglePolling();
    }

    public void SaveToken() => _generalSettingsService.DeepSeekApiToken = ApiToken;

    public async Task<double?> FetchBalanceAsync()
    {
        try
        {
            return await _deepSeekService.FetchBalanceAsync();
        }
        catch { }
        return null;
    }

    public void ClearHistory()
    {
        _deepSeekService.ClearHistory();
        BalanceHistory.Clear();
        DisplayHistory.Clear();
        UpdateHistoryCountText();
        HistoryChanged?.Invoke(this, EventArgs.Empty);
    }

    public void StartPolling()
    {
        _deepSeekService.StartPolling();
        _generalSettingsService.EnableDeepSeekBalanceMonitor = true;
        IsEnabled = true;
        UpdateStatusText();
        StatusChanged?.Invoke(this, EventArgs.Empty);
    }

    public void StopPolling()
    {
        _deepSeekService.StopPolling();
        _generalSettingsService.EnableDeepSeekBalanceMonitor = false;
        IsEnabled = false;
        UpdateStatusText();
        StatusChanged?.Invoke(this, EventArgs.Empty);
    }

    public void TogglePolling()
    {
        if (IsEnabled) StopPolling();
        else StartPolling();
    }

    private void OnServiceStatusChanged()
    {
        IsEnabled = _deepSeekService.IsPolling;
        UpdateStatusText();
        StatusChanged?.Invoke(this, EventArgs.Empty);
    }

    private void OnServiceBalanceUpdated(double balance)
    {
        lock (_deepSeekService.BalanceHistory)
        {
            var last = _deepSeekService.BalanceHistory.LastOrDefault();
            if (last != null)
            {
                App.MainWindow.DispatcherQueue.TryEnqueue(() =>
                {
                    BalanceHistory.Add(last);
                    CurrentBalanceText = $"当前余额：{last.Balance:F2} CNY";
                    UpdateHistoryCountText();
                    UpdateDisplayHistory();
                    BalanceUpdated?.Invoke(this, last);
                });
            }
        }
    }

    partial void OnIsEnabledChanged(bool value)
    {
        if (value && !_deepSeekService.IsPolling)
            StartPolling();
        else if (!value && _deepSeekService.IsPolling)
            StopPolling();
    }

    private void UpdateStatusText() =>
        StatusText = IsEnabled ? "状态：监控中" : "状态：已停止";

    private void UpdateHistoryCountText() =>
        HistoryCountText = BalanceHistory.Count > 0 ? $"共 {BalanceHistory.Count} 条记录" : "暂无历史数据";

    partial void OnIsCollapsedChanged(bool value)
    {
        _generalSettingsService.DeepSeekBalanceHistoryCollapsed = value;
        UpdateDisplayHistory();
    }

    private async void UpdateDisplayHistory()
    {
        var currentItems = BalanceHistory.ToList();
        bool isCollapsed = IsCollapsed;

        List<BalanceHistoryItem> processedItems = await Task.Run(() =>
        {
            var result = new List<BalanceHistoryItem>();

            if (!isCollapsed)
            {
                foreach (var item in currentItems)
                    result.Add(new BalanceHistoryItem
                    {
                        Time = item.Time,
                        Balance = item.Balance,
                        Change = item.Change,
                        ChangeType = item.ChangeType,
                        MergeCount = 1
                    });
            }
            else
            {
                BalanceHistoryItem? mergedItem = null;
                foreach (var item in currentItems)
                {
                    if (mergedItem == null)
                    {
                        mergedItem = new BalanceHistoryItem
                        {
                            Time = item.Time,
                            Balance = item.Balance,
                            Change = item.Change,
                            ChangeType = item.ChangeType,
                            MergeCount = 1
                        };
                    }
                    else if (mergedItem.ChangeType == item.ChangeType)
                    {
                        mergedItem.Time = item.Time;
                        mergedItem.Balance = item.Balance;
                        mergedItem.Change += item.Change;
                        mergedItem.MergeCount++;
                    }
                    else
                    {
                        result.Add(mergedItem);
                        mergedItem = new BalanceHistoryItem
                        {
                            Time = item.Time,
                            Balance = item.Balance,
                            Change = item.Change,
                            ChangeType = item.ChangeType,
                            MergeCount = 1
                        };
                    }
                }
                if (mergedItem != null) result.Add(mergedItem);
            }
            return result;
        });

        App.MainWindow.DispatcherQueue.TryEnqueue(() =>
        {
            DisplayHistory.Clear();
            foreach (var item in processedItems)
                DisplayHistory.Add(item);
        });
    }
}
