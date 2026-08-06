namespace NotifyRelay.Models.Render;

public class SuperIslandState
{
    public string? Title { get; set; }
    public string? Subtitle { get; set; }
    public string? AdditionalText { get; set; }
    public string? Extra { get; set; }
    public bool HasExtra => !string.IsNullOrEmpty(Extra);
    public byte[]? IconPng { get; set; }
    public Dictionary<string, string>? Pics { get; set; }

    public int Progress { get; set; }
    public bool HasProgress => Progress > 0;

    public TimerType TimerType { get; set; }
    public long TimerValue { get; set; }
    public long TimerStartTime { get; set; }

    public long LastUpdateTime { get; set; }

    /// <summary>
    /// 原始 param_v2_raw JSON，用于后续增量合并解析
    /// </summary>
    public string? ParamV2Raw { get; set; }

    /// <summary>
    /// 增量变更 JSON（"changes" 字段内容），用于 delta 合并
    /// </summary>
    public string? ChangesJson { get; set; }

    /// <summary>
    /// 将增量变更合并到当前状态中
    /// </summary>
    public void MergeChanges(string? changesJson)
    {
        if (string.IsNullOrWhiteSpace(changesJson))
            return;

        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(changesJson);
            var root = doc.RootElement;

            // 更新 ParamV2Raw（如果 changes 包含新的）
            if (root.TryGetProperty("param_v2_raw", out var p2r) && p2r.ValueKind == System.Text.Json.JsonValueKind.String)
            {
                ParamV2Raw = p2r.GetString();
            }

            // 重新解析 param_v2_raw
            SuperIslandParamV2Parser.ApplyToState(this, ParamV2Raw);

            LastUpdateTime = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        }
        catch (System.Text.Json.JsonException)
        {
            // 静默忽略
        }
    }

    public string GetDisplayTime()
    {
        if (TimerValue <= 0 && TimerType == TimerType.None) return string.Empty;

        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var elapsed = (now - TimerStartTime) / 1000;

        return TimerType switch
        {
            TimerType.CountUp => FormatTime(TimerValue + elapsed),
            TimerType.CountDown => FormatTime(Math.Max(0, TimerValue - elapsed)),
            TimerType.ActiveCountdown => FormatTime(Math.Max(0, TimerValue - elapsed)),
            TimerType.RelativeCount => FormatTime(TimerValue),
            _ => string.Empty
        };
    }

    /// <summary>
    /// 获取进度百分比文本（如 "67%"）
    /// </summary>
    public string? GetProgressText()
    {
        if (!HasProgress) return null;
        return $"{Math.Clamp(Progress, 0, 100)}%";
    }

    private static string FormatTime(long seconds)
    {
        var ts = TimeSpan.FromSeconds(seconds);
        if (ts.Hours > 0)
            return $"{ts.Hours}:{ts.Minutes:D2}:{ts.Seconds:D2}";
        return $"{ts.Minutes}:{ts.Seconds:D2}";
    }
}

public enum TimerType
{
    None = 0,
    CountUp = 1,
    CountDown = 2,
    ActiveCountdown = -1,
    RelativeCount = -2
}
