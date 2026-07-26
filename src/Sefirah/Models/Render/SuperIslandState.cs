namespace NotifyRelay.Models.Render;

public class SuperIslandState
{
    public string? Title { get; set; }
    public string? Subtitle { get; set; }
    public string? AdditionalText { get; set; }
    public byte[]? IconPng { get; set; }

    public int Progress { get; set; }
    public bool HasProgress => Progress > 0;

    public TimerType TimerType { get; set; }
    public long TimerValue { get; set; }
    public long TimerStartTime { get; set; }

    public long LastUpdateTime { get; set; }

    public string GetDisplayTime()
    {
        if (TimerValue <= 0) return string.Empty;

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
