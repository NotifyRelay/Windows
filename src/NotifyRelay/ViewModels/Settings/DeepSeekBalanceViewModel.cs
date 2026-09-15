using System.Runtime.CompilerServices;
using Microsoft.UI.Dispatching;
using NotifyRelay.Data.Contracts;
using NotifyRelay.Services.Overlay;
using NotifyRelay.Worker.Services;

namespace NotifyRelay.ViewModels.Settings;

/// <summary>
/// 覆盖层 - DeepSeek 余额子页 ViewModel。
/// 职责：余额监控开关（开 = 轮询 + 叠加层显示）、API Token、查询间隔、历史记录，
/// 以及余额卡片的显示设置（目标屏幕 / X、Y 位置 / 缩放），并推送到覆盖层渲染服务。
/// </summary>
public class DeepSeekBalanceViewModel : INotifyPropertyChanged
{
    private readonly IGeneralSettingsService _generalSettingsService;
    private readonly DeepSeekBalanceService _deepSeekService;
    private readonly OverlayRenderService? _renderService;
    private DispatcherQueue? _dispatcher;

    public DeepSeekBalanceViewModel()
    {
        _generalSettingsService = Ioc.Default.GetRequiredService<IGeneralSettingsService>();
        _deepSeekService = Ioc.Default.GetRequiredService<DeepSeekBalanceService>();
        _renderService = Ioc.Default.GetService<OverlayRenderService>();
        _dispatcher = DispatcherQueue.GetForCurrentThread();

        BuildScreenOptions();

        _deepSeekService.StatusChanged += OnServiceStatusChanged;
        _deepSeekService.BalanceUpdated += OnServiceBalanceUpdated;

        LoadFromSettings();
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    // ===== 历史记录 =====

    public ObservableCollection<BalanceHistoryItem> BalanceHistory { get; } = new();
    public ObservableCollection<BalanceHistoryItem> DisplayHistory { get; } = new();

    private string _apiToken = string.Empty;
    public string ApiToken
    {
        get => _apiToken;
        set { _apiToken = value; OnPropertyChanged(); }
    }

    private bool _isEnabled;
    /// <summary>余额监控开关：开 = 后台轮询 + 叠加层显示余额卡片。</summary>
    public bool IsEnabled
    {
        get => _isEnabled;
        set
        {
            if (_isEnabled == value) return;
            _isEnabled = value;
            OnPropertyChanged();
            ApplyEnabledChange(value);
        }
    }

    private string _currentBalanceText = "当前余额：-- CNY";
    public string CurrentBalanceText
    {
        get => _currentBalanceText;
        private set { _currentBalanceText = value; OnPropertyChanged(); }
    }

    private string _statusText = "状态：已停止";
    public string StatusText
    {
        get => _statusText;
        private set { _statusText = value; OnPropertyChanged(); }
    }

    private string _historyCountText = "暂无历史数据";
    public string HistoryCountText
    {
        get => _historyCountText;
        private set { _historyCountText = value; OnPropertyChanged(); }
    }

    private int _selectedIntervalIndex = 1;
    public int SelectedIntervalIndex
    {
        get => _selectedIntervalIndex;
        set
        {
            if (_selectedIntervalIndex == value) return;
            _selectedIntervalIndex = value;
            OnPropertyChanged();
            _generalSettingsService.DeepSeekBalancePollingInterval = GetIntervalMs(value);
            // 间隔变更需重启轮询才能生效（保持开启状态不变）
            RestartPollingIfEnabled();
        }
    }

    private bool _isCollapsed;
    public bool IsCollapsed
    {
        get => _isCollapsed;
        set
        {
            if (_isCollapsed == value) return;
            _isCollapsed = value;
            OnPropertyChanged();
            _generalSettingsService.DeepSeekBalanceHistoryCollapsed = value;
            UpdateDisplayHistory();
        }
    }

    // ===== 叠加层显示设置 =====

    public List<ScreenOption> Screens { get; } = [];

    private ScreenOption? _selectedScreen;
    public ScreenOption? SelectedScreen
    {
        get => _selectedScreen;
        set
        {
            _selectedScreen = value;
            if (value != null) _generalSettingsService.DeepSeekBalanceTargetScreen = value.Id;
            OnPropertyChanged();
            PushConfig();
        }
    }

    public int XPercent
    {
        get => _generalSettingsService.DeepSeekBalanceXPercent;
        set { _generalSettingsService.DeepSeekBalanceXPercent = value; OnPropertyChanged(); PushConfig(); }
    }

    public int YPercent
    {
        get => _generalSettingsService.DeepSeekBalanceYPercent;
        set { _generalSettingsService.DeepSeekBalanceYPercent = value; OnPropertyChanged(); PushConfig(); }
    }

    public float Scale
    {
        get => _generalSettingsService.DeepSeekBalanceScale;
        set { _generalSettingsService.DeepSeekBalanceScale = value; OnPropertyChanged(); PushConfig(); }
    }

    // ===== 初始化 =====

    /// <summary>从设置回显各字段，并回显已按设置自动启动的服务实际运行状态。</summary>
    public void LoadFromSettings()
    {
        ApiToken = _generalSettingsService.DeepSeekApiToken ?? string.Empty;
        _selectedIntervalIndex = GetIntervalIndex(_generalSettingsService.DeepSeekBalancePollingInterval);
        OnPropertyChanged(nameof(SelectedIntervalIndex));
        _isCollapsed = _generalSettingsService.DeepSeekBalanceHistoryCollapsed;
        OnPropertyChanged(nameof(IsCollapsed));
        // 直接回显服务运行状态，避免走 IsEnabled 的 setter 触发启停
        _isEnabled = _deepSeekService.IsPolling;
        OnPropertyChanged(nameof(IsEnabled));
        UpdateStatusText();

        // 载入已有历史并同步到 UI
        SyncHistoryFromService();
        PushConfig();
    }

    private void BuildScreenOptions()
    {
        _selectedScreen = OverlayScreenOptions.BuildInto(
            Screens, _renderService, _generalSettingsService.DeepSeekBalanceTargetScreen);
    }

    // ===== 操作 =====

    public void SaveToken()
    {
        _generalSettingsService.DeepSeekApiToken = ApiToken;
        // Token 变更后若正在监控，重启轮询以便新 Token 立即生效
        RestartPollingIfEnabled();
    }

    public async Task<double?> FetchBalanceAsync()
    {
        try
        {
            var balance = await _deepSeekService.FetchBalanceAsync();
            if (balance.HasValue) SyncHistoryFromService();
            return balance;
        }
        catch { }
        return null;
    }

    public void ClearHistory()
    {
        _deepSeekService.ClearHistory();
        RunOnUi(() =>
        {
            BalanceHistory.Clear();
            DisplayHistory.Clear();
            UpdateHistoryCountText();
        });
    }

    public void StartPolling()
    {
        _deepSeekService.StartPolling();
        _generalSettingsService.EnableDeepSeekBalanceMonitor = true;
        _isEnabled = true;
        OnPropertyChanged(nameof(IsEnabled));
        UpdateStatusText();
        PushConfig();
    }

    public void StopPolling()
    {
        _deepSeekService.StopPolling();
        _generalSettingsService.EnableDeepSeekBalanceMonitor = false;
        _isEnabled = false;
        OnPropertyChanged(nameof(IsEnabled));
        UpdateStatusText();
        PushConfig();
    }

    /// <summary>
    /// 重启轮询以让间隔 / Token 变更生效；仅当当前处于开启状态时执行，
    /// 且保持开启状态不变（不得因重启而关闭开关，否则会连带隐藏叠加层卡片）。
    /// </summary>
    private void RestartPollingIfEnabled()
    {
        if (!_isEnabled) return;
        _deepSeekService.StopPolling();
        _deepSeekService.StartPolling();
        UpdateStatusText();
        PushConfig();
    }

    private void ApplyEnabledChange(bool value)
    {
        if (value) StartPolling();
        else StopPolling();
    }

    private void OnServiceStatusChanged()
    {
        RunOnUi(() =>
        {
            _isEnabled = _deepSeekService.IsPolling;
            OnPropertyChanged(nameof(IsEnabled));
            UpdateStatusText();
        });
    }

    private void OnServiceBalanceUpdated(double balance) => SyncHistoryFromService();

    /// <summary>把服务端历史快照同步到 UI 集合（整体替换，避免增量错位）。</summary>
    private void SyncHistoryFromService()
    {
        List<BalanceHistoryItem> snapshot;
        lock (_deepSeekService.BalanceHistory)
        {
            snapshot = _deepSeekService.BalanceHistory
                .Select(x => new BalanceHistoryItem
                {
                    Time = x.Time,
                    Balance = x.Balance,
                    Change = x.Change,
                    ChangeType = x.ChangeType,
                    MergeCount = x.MergeCount
                })
                .ToList();
        }

        RunOnUi(() =>
        {
            BalanceHistory.Clear();
            foreach (var item in snapshot) BalanceHistory.Add(item);

            var last = snapshot.LastOrDefault();
            CurrentBalanceText = last != null ? $"当前余额：{last.Balance:F2} CNY" : "当前余额：-- CNY";
            UpdateHistoryCountText();
            UpdateDisplayHistory();
        });
    }

    private void UpdateStatusText() =>
        StatusText = _deepSeekService.IsPolling ? "状态：监控中" : "状态：已停止";

    private void UpdateHistoryCountText() =>
        HistoryCountText = BalanceHistory.Count > 0 ? $"共 {BalanceHistory.Count} 条记录" : "暂无历史数据";

    /// <summary>把当前显示配置推送到覆盖层渲染服务。</summary>
    public void PushConfig()
    {
        _renderService?.SetDeepSeekBalanceConfig(
            _generalSettingsService.EnableDeepSeekBalanceMonitor,
            _generalSettingsService.DeepSeekBalanceTargetScreen,
            _generalSettingsService.DeepSeekBalanceXPercent,
            _generalSettingsService.DeepSeekBalanceYPercent,
            _generalSettingsService.DeepSeekBalanceScale);
    }

    private void UpdateDisplayHistory()
    {
        var currentItems = BalanceHistory.ToList();
        bool isCollapsed = IsCollapsed;

        List<BalanceHistoryItem> processedItems = [];
        if (!isCollapsed)
        {
            processedItems.AddRange(currentItems);
        }
        else
        {
            // 折叠模式：合并相邻同类型变化，累加变化量与计数
            BalanceHistoryItem? merged = null;
            foreach (var item in currentItems)
            {
                if (merged == null || merged.ChangeType != item.ChangeType)
                {
                    if (merged != null) processedItems.Add(merged);
                    merged = new BalanceHistoryItem
                    {
                        Time = item.Time,
                        Balance = item.Balance,
                        Change = item.Change,
                        ChangeType = item.ChangeType,
                        MergeCount = item.MergeCount
                    };
                }
                else
                {
                    merged.Time = item.Time;
                    merged.Balance = item.Balance;
                    merged.Change += item.Change;
                    merged.MergeCount += item.MergeCount;
                }
            }
            if (merged != null) processedItems.Add(merged);
        }

        RunOnUi(() =>
        {
            DisplayHistory.Clear();
            foreach (var item in processedItems) DisplayHistory.Add(item);
        });
    }

    private static int GetIntervalIndex(int intervalMs) => intervalMs switch
    {
        1000 => 0,
        60000 => 1,
        1800000 => 2,
        86400000 => 3,
        _ => 1
    };

    private static int GetIntervalMs(int index) => index switch
    {
        0 => 1000,
        1 => 60000,
        2 => 1800000,
        3 => 86400000,
        _ => 60000
    };

    private void RunOnUi(Action action)
    {
        var dispatcher = _dispatcher ??= DispatcherQueue.GetForCurrentThread();
        if (dispatcher != null && !dispatcher.HasThreadAccess)
            dispatcher.TryEnqueue(() => action());
        else if (dispatcher != null)
            action();
        else
            action();
    }

    protected void OnPropertyChanged([CallerMemberName] string propertyName = "")
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
