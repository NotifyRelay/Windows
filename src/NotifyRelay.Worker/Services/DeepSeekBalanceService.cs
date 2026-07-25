using System.Text.Json;
using Microsoft.Extensions.Logging;
using NotifyRelay.Worker.Configuration;

namespace NotifyRelay.Worker.Services;

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
    private readonly ILogger _logger;
    private readonly WorkerConfiguration _config;
    private readonly HttpClient _httpClient;
    private CancellationTokenSource? _pollingCts;
    private bool _isPolling;
    private const int MaxHistoryItems = 500;

    public event Action<double>? BalanceUpdated;
    public event Action? StatusChanged;

    public bool IsPolling => _isPolling;
    public double CurrentBalance { get; private set; }
    public List<BalanceHistoryItem> BalanceHistory { get; } = [];

    public DeepSeekBalanceService(ILogger logger, WorkerConfiguration config)
    {
        _logger = logger;
        _config = config;
        _httpClient = new HttpClient();
        _ = LoadHistoryAsync();
    }

    public void StartPolling()
    {
        if (_isPolling) return;

        if (string.IsNullOrEmpty(_config.DeepSeekApiToken))
        {
            _logger.LogWarning("DeepSeek API Token not set");
            return;
        }

        try
        {
            _pollingCts = new CancellationTokenSource();
            _isPolling = true;
            _logger.LogInformation("DeepSeek balance polling started");

            _ = Task.Run(async () =>
            {
                while (!_pollingCts.Token.IsCancellationRequested)
                {
                    try
                    {
                        var balance = await FetchBalanceAsync();
                        if (balance.HasValue)
                        {
                            CurrentBalance = balance.Value;
                            var item = new BalanceHistoryItem
                            {
                                Time = DateTime.Now,
                                Balance = balance.Value
                            };
                            AddHistoryItem(item);
                            BalanceUpdated?.Invoke(balance.Value);
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Failed to fetch DeepSeek balance");
                    }

                    try
                    {
                        await Task.Delay(_config.DeepSeekBalancePollingInterval, _pollingCts.Token);
                    }
                    catch (TaskCanceledException) { break; }
                }
            });

            StatusChanged?.Invoke();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to start DeepSeek balance polling");
            _isPolling = false;
        }
    }

    public void StopPolling()
    {
        if (!_isPolling) return;

        try
        {
            _pollingCts?.Cancel();
            _pollingCts?.Dispose();
            _pollingCts = null;
            _isPolling = false;
            _logger.LogInformation("DeepSeek balance polling stopped");
            StatusChanged?.Invoke();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to stop DeepSeek balance polling");
        }
    }

    public async Task<double?> FetchBalanceAsync()
    {
        var token = _config.DeepSeekApiToken;
        if (string.IsNullOrEmpty(token)) return null;

        try
        {
            var request = new HttpRequestMessage(HttpMethod.Get, "https://api.deepseek.com/user/balance");
            request.Headers.Add("Authorization", $"Bearer {token}");
            request.Headers.Add("Accept", "application/json");

            var response = await _httpClient.SendAsync(request);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogError("DeepSeek API returned {StatusCode}", response.StatusCode);
                return null;
            }

            var json = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(json);

            if (doc.RootElement.TryGetProperty("is_available", out var isAvailable) &&
                isAvailable.ValueKind == JsonValueKind.False)
            {
                _logger.LogWarning("DeepSeek account has no available balance");
                return 0;
            }

            if (doc.RootElement.TryGetProperty("balance_infos", out var balanceInfos) &&
                balanceInfos.ValueKind == JsonValueKind.Array &&
                balanceInfos.GetArrayLength() > 0)
            {
                foreach (var balanceInfo in balanceInfos.EnumerateArray())
                {
                    if (balanceInfo.TryGetProperty("currency", out var currency) &&
                        currency.GetString() == "CNY" &&
                        balanceInfo.TryGetProperty("total_balance", out var totalBalance))
                    {
                        return ParseBalance(totalBalance);
                    }
                }

                var firstBalance = balanceInfos[0];
                if (firstBalance.TryGetProperty("total_balance", out var firstTotalBalance))
                    return ParseBalance(firstTotalBalance);
            }

            _logger.LogWarning("Failed to parse DeepSeek balance response: {Json}", json);
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to fetch DeepSeek balance");
            return null;
        }
    }

    private static double? ParseBalance(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Number)
            return element.GetDouble();
        if (element.ValueKind == JsonValueKind.String &&
            double.TryParse(element.GetString(), out var balance))
            return balance;
        return null;
    }

    private void AddHistoryItem(BalanceHistoryItem item)
    {
        lock (BalanceHistory)
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
                    item.ChangeType = item.Change > 0 ? "+" : item.Change < 0 ? "-" : "=";
                }
                else
                {
                    item.Change = 0;
                    item.ChangeType = "init";
                }
                BalanceHistory.Add(item);
            }

            while (BalanceHistory.Count > MaxHistoryItems)
                MergeOldestConsecutiveItems();
        }

        SaveHistory();
    }

    private void MergeOldestConsecutiveItems()
    {
        if (BalanceHistory.Count < 2) return;

        int mergeCount = BalanceHistory.Count - MaxHistoryItems + 1;
        int startIndex = 0;

        while (startIndex < BalanceHistory.Count - 1 && mergeCount > 0)
        {
            int endIndex = startIndex;
            string currentType = BalanceHistory[startIndex].ChangeType;

            while (endIndex < BalanceHistory.Count &&
                   BalanceHistory[endIndex].ChangeType == currentType)
                endIndex++;

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
                    BalanceHistory.RemoveAt(i);
                BalanceHistory.Insert(startIndex, mergedItem);

                mergeCount -= consecutiveCount - 1;
                if (mergeCount <= 0) break;
            }
            startIndex++;
        }

        if (mergeCount > 0 && BalanceHistory.Count > MaxHistoryItems)
            BalanceHistory.RemoveAt(0);
    }

    public void ClearHistory()
    {
        lock (BalanceHistory)
        {
            BalanceHistory.Clear();
        }
        SaveHistory();
    }

    private async Task LoadHistoryAsync()
    {
        try
        {
            var json = _config.DeepSeekBalanceHistoryJson;
            if (string.IsNullOrEmpty(json)) return;

            var items = JsonSerializer.Deserialize<List<BalanceHistoryItem>>(json);
            if (items != null)
            {
                lock (BalanceHistory)
                {
                    BalanceHistory.Clear();
                    foreach (var item in items.OrderBy(x => x.Time))
                        BalanceHistory.Add(item);
                    if (BalanceHistory.Count > 0)
                        CurrentBalance = BalanceHistory.Last().Balance;
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load DeepSeek balance history");
        }
    }

    private void SaveHistory()
    {
        try
        {
            lock (BalanceHistory)
            {
                _config.DeepSeekBalanceHistoryJson = JsonSerializer.Serialize(BalanceHistory.ToList());
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save DeepSeek balance history");
        }
    }
}
