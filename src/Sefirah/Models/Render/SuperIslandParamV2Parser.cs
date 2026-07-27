namespace NotifyRelay.Models.Render;

/// <summary>
/// 解析超级岛 param_v2_raw JSON，提取 Extra 文本、进度、计时器等结构化信息。
/// 从 Gamebar SuperIslandViewModel 的 UpdateFromState 逻辑移植。
/// </summary>
public static class SuperIslandParamV2Parser
{
    /// <summary>
    /// 从 param_v2_raw JSON 中解析并填充 SuperIslandState。
    /// </summary>
    public static void ApplyToState(SuperIslandState state, string? paramV2Raw)
    {
        if (string.IsNullOrWhiteSpace(paramV2Raw))
            return;

        try
        {
            using var doc = JsonDocument.Parse(paramV2Raw);
            var root = doc.RootElement;

            // 提取 Extra 文本 —— 优先级：highlightInfo > chatInfo > baseInfo
            var extraParts = new List<string>();

            ExtractHighlightInfo(root, extraParts);
            ExtractChatInfo(root, extraParts);
            ExtractBaseInfo(root, extraParts);

            if (extraParts.Count > 0)
            {
                state.Extra = string.Join(" · ", extraParts);
            }

            // 提取进度
            ExtractProgress(root, state);

            // 提取计时器
            ExtractTimerInfo(root, state);
        }
        catch (JsonException)
        {
            // 解析失败静默忽略
        }
    }

    private static void ExtractHighlightInfo(JsonElement root, List<string> parts)
    {
        if (!root.TryGetProperty("highlightInfo", out var hi) || hi.ValueKind != JsonValueKind.Object)
            return;

        // 尝试提取标题/内容文本
        TryAppendString(hi, "title", parts);
        TryAppendString(hi, "content", parts);
        TryAppendString(hi, "text", parts);

        // 进度
        if (hi.TryGetProperty("progress", out var progress) && progress.ValueKind == JsonValueKind.Number)
        {
            TryAppendString(hi, "progressText", parts);
        }
    }

    private static void ExtractChatInfo(JsonElement root, List<string> parts)
    {
        if (!root.TryGetProperty("chatInfo", out var ci) || ci.ValueKind != JsonValueKind.Object)
            return;

        TryAppendString(ci, "title", parts);
        TryAppendString(ci, "content", parts);
        TryAppendString(ci, "text", parts);
        TryAppendString(ci, "sender", parts);
    }

    private static void ExtractBaseInfo(JsonElement root, List<string> parts)
    {
        if (!root.TryGetProperty("baseInfo", out var bi) || bi.ValueKind != JsonValueKind.Object)
            return;

        TryAppendString(bi, "title", parts);
        TryAppendString(bi, "content", parts);
        TryAppendString(bi, "text", parts);
    }

    private static void ExtractProgress(JsonElement root, SuperIslandState state)
    {
        // 优先 multiProgressInfo（多段进度取 max 或第一段）
        if (root.TryGetProperty("multiProgressInfo", out var mpi) && mpi.ValueKind == JsonValueKind.Array)
        {
            int maxProgress = 0;
            foreach (var seg in mpi.EnumerateArray())
            {
                if (seg.ValueKind == JsonValueKind.Object &&
                    seg.TryGetProperty("progress", out var p) && p.ValueKind == JsonValueKind.Number)
                {
                    int val = p.GetInt32();
                    if (val > maxProgress) maxProgress = val;
                }
            }
            if (maxProgress > 0)
            {
                state.Progress = maxProgress;
            }
            return;
        }

        // progressInfo
        if (root.TryGetProperty("progressInfo", out var pi) && pi.ValueKind == JsonValueKind.Object)
        {
            if (pi.TryGetProperty("progress", out var p) && p.ValueKind == JsonValueKind.Number)
            {
                state.Progress = p.GetInt32();
            }
        }
    }

    private static void ExtractTimerInfo(JsonElement root, SuperIslandState state)
    {
        if (!root.TryGetProperty("timerInfo", out var ti) || ti.ValueKind != JsonValueKind.Object)
            return;

        // timerType: -2=RelativeCount, -1=ActiveCountdown, 0=None, 1=CountUp, 2=CountDown
        if (ti.TryGetProperty("timerType", out var tt) && tt.ValueKind == JsonValueKind.Number)
        {
            state.TimerType = (TimerType)tt.GetInt32();
        }

        // timerWhen: 计时器基准时间戳（毫秒）
        if (ti.TryGetProperty("timerWhen", out var tw) && tw.ValueKind == JsonValueKind.Number)
        {
            state.TimerStartTime = tw.GetInt64();
        }

        // timerTotal: 计时器总时长（秒），仅倒计时有效
        if (ti.TryGetProperty("timerTotal", out var tt2) && tt2.ValueKind == JsonValueKind.Number)
        {
            state.TimerValue = tt2.GetInt64();
        }

        // timerSystemCurrent: 发送时系统时间（毫秒），用于本地计时修正
        // 与 timerWhen 配合计算已流逝时间
        if (ti.TryGetProperty("timerSystemCurrent", out var tsc) && tsc.ValueKind == JsonValueKind.Number)
        {
            // 修正 timerStartTime：基于本地当前时间推算
            long systemCurrent = tsc.GetInt64();
            long localNow = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            long offset = systemCurrent - state.TimerStartTime;
            state.TimerStartTime = localNow - offset;
        }
    }

    private static void TryAppendString(JsonElement element, string propertyName, List<string> parts)
    {
        if (element.TryGetProperty(propertyName, out var prop) &&
            prop.ValueKind == JsonValueKind.String)
        {
            var value = prop.GetString();
            if (!string.IsNullOrWhiteSpace(value))
            {
                parts.Add(value);
            }
        }
    }
}
