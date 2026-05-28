using NotifyRelay.Data.Contracts;

namespace NotifyRelay.DeviceCtrl.DeepSeekBalance;

public class BalanceHistoryItem
{
    public DateTime Time { get; set; }
    public double Balance { get; set; }
    public double Change { get; set; }
    public string ChangeType { get; set; } = string.Empty;
    public int MergeCount { get; set; } = 1;
}

public class DeepSeekBalanceService
{
    private readonly ILogger<DeepSeekBalanceService> _logger;
    private readonly IGeneralSettingsService _generalSettingsService;
    private readonly HttpClient _httpClient;
    private CancellationTokenSource? _pollingCts;
    private bool _isPolling;
    private const int MaxHistoryItems = 500;

    public event EventHandler<BalanceHistoryItem>? BalanceUpdated;
    public event EventHandler? StatusChanged;
    public event EventHandler? HistoryChanged;

    public bool IsPolling => _isPolling;

    public ObservableCollection<BalanceHistoryItem> BalanceHistory { get; } = new();

    public double CurrentBalance { get; private set; }

    public DeepSeekBalanceService(ILogger<DeepSeekBalanceService> logger, IGeneralSettingsService generalSettingsService)
    {
        _logger = logger;
        _generalSettingsService = generalSettingsService;
        _httpClient = new HttpClient();
        _ = LoadHistoryAsync();
    }

    public void StartPolling()
    {
        if (_isPolling)
        {
            _logger.LogInformation("DeepSeek余额监控已经在运行");
            return;
        }

        if (string.IsNullOrEmpty(_generalSettingsService.DeepSeekApiToken))
        {
            _logger.LogWarning("DeepSeek API Token未设置");
            return;
        }

        try
        {
            _pollingCts = new CancellationTokenSource();
            _isPolling = true;
            _generalSettingsService.EnableDeepSeekBalanceMonitor = true;
            _logger.LogInformation("DeepSeek余额监控已启动");

            Task.Run(async () =>
            {
                while (!_pollingCts.Token.IsCancellationRequested)
                {
                    try
                    {
                        var balance = await FetchBalanceAsync();
                        if (balance.HasValue)
                        {
                            CurrentBalance = balance.Value;
                            AddHistoryItem(new BalanceHistoryItem
                            {
                                Time = DateTime.Now,
                                Balance = balance.Value
                            });
                            BalanceUpdated?.Invoke(this, new BalanceHistoryItem
                            {
                                Time = DateTime.Now,
                                Balance = balance.Value
                            });
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "获取DeepSeek余额失败");
                    }

                    try
                    {
                        await Task.Delay(_generalSettingsService.DeepSeekBalancePollingInterval, _pollingCts.Token);
                    }
                    catch (TaskCanceledException)
                    {
                        break;
                    }
                }
            });

            StatusChanged?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "启动DeepSeek余额监控失败");
            _isPolling = false;
        }
    }

    public void StopPolling()
    {
        if (!_isPolling)
        {
            _logger.LogInformation("DeepSeek余额监控未运行");
            return;
        }

        try
        {
            _pollingCts?.Cancel();
            _pollingCts?.Dispose();
            _pollingCts = null;
            _isPolling = false;
            _generalSettingsService.EnableDeepSeekBalanceMonitor = false;
            _logger.LogInformation("DeepSeek余额监控已停止");
            StatusChanged?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "停止DeepSeek余额监控失败");
        }
    }

    public async Task<double?> FetchBalanceAsync()
    {
        var token = _generalSettingsService.DeepSeekApiToken;
        if (string.IsNullOrEmpty(token))
        {
            _logger.LogWarning("DeepSeek API Token未设置");
            return null;
        }

        try
        {
            var request = new HttpRequestMessage(HttpMethod.Get, "https://api.deepseek.com/user/balance");
            request.Headers.Add("Authorization", $"Bearer {token}");
            request.Headers.Add("Accept", "application/json");

            var response = await _httpClient.SendAsync(request);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogError($"DeepSeek API返回错误: {response.StatusCode}");
                return null;
            }

            var json = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(json);

            if (doc.RootElement.TryGetProperty("is_available", out var isAvailable) &&
                isAvailable.ValueKind == JsonValueKind.False)
            {
                _logger.LogWarning("DeepSeek账户当前没有可用余额");
                return 0;
            }

            if (doc.RootElement.TryGetProperty("balance_infos", out var balanceInfos) &&
                balanceInfos.ValueKind == JsonValueKind.Array &&
                balanceInfos.GetArrayLength() > 0)
            {
                foreach (var balanceInfo in balanceInfos.EnumerateArray())
                {
                    if (balanceInfo.TryGetProperty("currency", out var currency) &&
                        currency.GetString() == "CNY")
                    {
                        if (balanceInfo.TryGetProperty("total_balance", out var totalBalance))
                        {
                            return ParseBalance(totalBalance);
                        }
                    }
                }

                var firstBalance = balanceInfos[0];
                if (firstBalance.TryGetProperty("total_balance", out var firstTotalBalance))
                {
                    return ParseBalance(firstTotalBalance);
                }
            }

            _logger.LogWarning("无法解析DeepSeek余额响应: {Json}", json);
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "获取DeepSeek余额失败");
            return null;
        }
    }

    private double? ParseBalance(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Number)
        {
            return element.GetDouble();
        }
        else if (element.ValueKind == JsonValueKind.String)
        {
            var balanceStr = element.GetString();
            if (double.TryParse(balanceStr, out var balance))
            {
                return balance;
            }
        }
        return null;
    }

    private void AddHistoryItem(BalanceHistoryItem item)
    {
        App.MainWindow.DispatcherQueue.TryEnqueue(() =>
        {
            var last = BalanceHistory.LastOrDefault();

            if (last != null && Math.Abs(last.Balance - item.Balance) < 0.0001)
            {
                last.Time = item.Time;
            }
            else
            {
                if (last != null)
                {
                    item.Change = item.Balance - last.Balance;
                    item.ChangeType = item.Change > 0 ? "增加" : item.Change < 0 ? "减少" : "不变";
                }
                else
                {
                    item.Change = 0;
                    item.ChangeType = "初始";
                }
                BalanceHistory.Add(item);
            }

            while (BalanceHistory.Count > MaxHistoryItems)
            {
                MergeOldestConsecutiveItems();
            }

            SaveHistory();
            HistoryChanged?.Invoke(this, EventArgs.Empty);
        });
    }

    private void MergeOldestConsecutiveItems()
    {
        if (BalanceHistory.Count < 2)
            return;

        int mergeCount = BalanceHistory.Count - MaxHistoryItems + 1;
        
        int startIndex = 0;
        while (startIndex < BalanceHistory.Count - 1 && mergeCount > 0)
        {
            int endIndex = startIndex;
            string currentType = BalanceHistory[startIndex].ChangeType;
            
            while (endIndex < BalanceHistory.Count && 
                   BalanceHistory[endIndex].ChangeType == currentType)
            {
                endIndex++;
            }
            
            int consecutiveCount = endIndex - startIndex;
            if (consecutiveCount >= 2)
            {
                double totalChange = 0;
                int totalMergeCount = 0;
                for (int i = startIndex; i < endIndex; i++)
                {
                    totalChange += BalanceHistory[i].Change;
                    totalMergeCount += BalanceHistory[i].MergeCount;
                }
                
                var mergedItem = new BalanceHistoryItem
                {
                    Time = BalanceHistory[endIndex - 1].Time,
                    Balance = BalanceHistory[endIndex - 1].Balance,
                    Change = totalChange,
                    ChangeType = currentType,
                    MergeCount = totalMergeCount
                };
                
                for (int i = endIndex - 1; i >= startIndex; i--)
                {
                    BalanceHistory.RemoveAt(i);
                }
                BalanceHistory.Insert(startIndex, mergedItem);
                
                mergeCount -= consecutiveCount - 1;
                if (mergeCount <= 0)
                    break;
            }
            
            startIndex++;
        }
        
        if (mergeCount > 0 && BalanceHistory.Count > MaxHistoryItems)
        {
            BalanceHistory.RemoveAt(0);
        }
    }

    public void ClearHistory()
    {
        App.MainWindow.DispatcherQueue.TryEnqueue(() =>
        {
            BalanceHistory.Clear();
            SaveHistory();
            HistoryChanged?.Invoke(this, EventArgs.Empty);
        });
    }

    private async Task LoadHistoryAsync()
    {
        try
        {
            var json = _generalSettingsService.DeepSeekBalanceHistoryJson;
            if (string.IsNullOrEmpty(json)) return;

            List<BalanceHistoryItem>? items = null;
            await Task.Run(() =>
            {
                items = JsonSerializer.Deserialize<List<BalanceHistoryItem>>(json);
            });

            if (items != null)
            {
                var orderedItems = items.OrderBy(x => x.Time).ToList();
                App.MainWindow.DispatcherQueue.TryEnqueue(() =>
                {
                    BalanceHistory.Clear();
                    foreach (var item in orderedItems)
                    {
                        BalanceHistory.Add(item);
                    }
                    if (BalanceHistory.Count > 0)
                    {
                        CurrentBalance = BalanceHistory.Last().Balance;
                    }
                    HistoryChanged?.Invoke(this, EventArgs.Empty);
                });
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "加载DeepSeek余额历史失败");
        }
    }

    private void SaveHistory()
    {
        try
        {
            var json = JsonSerializer.Serialize(BalanceHistory.ToList());
            _generalSettingsService.DeepSeekBalanceHistoryJson = json;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "保存DeepSeek余额历史失败");
        }
    }

    public void TogglePolling()
    {
        if (_isPolling)
            StopPolling();
        else
            StartPolling();
    }
}
