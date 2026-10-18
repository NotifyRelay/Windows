using System.Text.Json;

namespace NotifyRelay.Models.Render;

/// <summary>
/// 解析超级岛 param_v2 JSON，提取结构化模型（ParamV2）及简化状态字段。
/// 从 Android superislandui 的 ParamV2Renderer / AParser / BParser / ParamIslandData 移植。
/// </summary>
public static partial class SuperIslandParamV2Parser
{
    /// <summary>
    /// 解析 param_v2 JSON 为完整结构化模型；解析失败返回 null。
    /// </summary>
    public static ParamV2? ParseParamV2(string? paramV2Raw)
    {
        if (string.IsNullOrWhiteSpace(paramV2Raw)) return null;

        try
        {
            using var doc = JsonDocument.Parse(paramV2Raw);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return null;

            var result = new ParamV2
            {
                Business = GetString(root, "business")?.TrimOrNull(),
                AodPic = GetString(root, "aodPic")?.TrimOrNull(),
                BaseInfo = TryParse(() => ParseBaseInfo(root)),
                ChatInfo = TryParse(() => ParseChatInfo(root)),
                AnimTextInfo = TryParse(() => ParseAnimTextInfo(root)),
                PicInfo = TryParse(() => ParsePicInfo(root)),
                ProgressInfo = TryParse(() => ParseProgressInfo(root)),
                MultiProgressInfo = TryParse(() => ParseMultiProgressInfo(root)),
                Actions = TryParse(() => ParseActions(root)),
                HintInfo = TryParse(() => ParseHintInfo(root)),
                TextButton = TryParse(() => ParseTextButton(root)),
                IconTextInfo = TryParse(() => ParseIconTextInfo(root)),
                CoverInfo = TryParse(() => ParseCoverInfo(root)),
                HighlightInfoV3 = TryParse(() => ParseHighlightInfoV3(root)),
                BgInfo = TryParse(() => ParseBgInfo(root)),
            };

            // highlightInfo 独立解析（iconTextInfo 已作为独立组件 ParseIconTextInfo 处理）
            result.HighlightInfo = TryParse(() => ParseHighlightInfo(root));

            // 提取 aodPic 与 picFunction，供 A/B 区图标键解析
            var highlightPicFunction = result.HighlightInfo?.PicFunction;
            var picFunction = highlightPicFunction ?? GetString(root, "picFunction")?.TrimOrNull();
            result.PicFunction = picFunction;

            result.ParamIsland = TryParse(() => ParseParamIsland(root, picFunction, result.AodPic));

            // multiProgressInfo 为空但 progressInfo 含节点资源时转换为 multiProgressInfo
            if (result.MultiProgressInfo == null && result.ProgressInfo != null)
            {
                result.MultiProgressInfo = result.ProgressInfo.ToMultiProgressInfo(
                    result.BaseInfo?.Title?.TrimOrNull());
            }

            return result;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// 从 param_v2_raw JSON 中解析并填充 SuperIslandState（保留原有简化行为）。
    /// </summary>
    public static void ApplyToState(SuperIslandState state, string? paramV2Raw)
    {
        if (string.IsNullOrWhiteSpace(paramV2Raw)) return;

        var parsed = ParseParamV2(paramV2Raw);
        state.ParamV2 = parsed;
        if (parsed == null) return;

        // 提取 Extra 文本 —— 优先级：highlightInfo > chatInfo > baseInfo
        var extraParts = new List<string>();
        AppendInfoTexts(parsed.HighlightInfo, extraParts);
        AppendInfoTexts(parsed.ChatInfo, extraParts);
        AppendInfoTexts(parsed.BaseInfo, extraParts);

        if (extraParts.Count > 0)
        {
            state.Extra = string.Join(" · ", extraParts);
        }

        // 提取进度
        if (parsed.MultiProgressInfo != null)
        {
            if (parsed.MultiProgressInfo.Progress > 0)
                state.Progress = parsed.MultiProgressInfo.Progress;
        }
        else if (parsed.ProgressInfo != null)
        {
            state.Progress = parsed.ProgressInfo.Progress;
        }

        // 提取计时器（对齐 Android TimerInfo 语义：-2 倒计时暂停 / -1 倒计时运行 / 1 正计时运行 / 2 正计时暂停）
        var timer = FirstTimerInfo(parsed);
        if (timer != null)
        {
            switch (timer.TimerType)
            {
                case -2: // 倒计时暂停：固定显示剩余 (timerWhen - timerSystemCurrent)
                    state.TimerType = TimerType.RelativeCount;
                    state.TimerValue = 0;
                    state.PausedSeconds = Math.Max(0, (timer.TimerWhen - timer.TimerSystemCurrent) / 1000);
                    break;
                case 2: // 正计时暂停：固定显示已计 (timerSystemCurrent - timerWhen)
                    state.TimerType = TimerType.CountDown;
                    state.TimerValue = 0;
                    state.PausedSeconds = Math.Max(0, (timer.TimerSystemCurrent - timer.TimerWhen) / 1000);
                    break;
                case -1: // 倒计时运行中
                    state.TimerType = TimerType.ActiveCountdown;
                    state.TimerValue = timer.TimerTotal;
                    state.PausedSeconds = 0;
                    ApplyTimerBase(state, timer);
                    break;
                case 1: // 正计时运行中
                    state.TimerType = TimerType.CountUp;
                    state.TimerValue = timer.TimerTotal;
                    state.PausedSeconds = 0;
                    ApplyTimerBase(state, timer);
                    break;
                default:
                    state.TimerType = TimerType.None;
                    state.PausedSeconds = 0;
                    break;
            }
        }
    }

    /// <summary>修正计时基准：基于本地当前时间推算 TimerStartTime。</summary>
    private static void ApplyTimerBase(SuperIslandState state, TimerInfoData timer)
    {
        if (timer.TimerSystemCurrent > 0 && timer.TimerWhen > 0)
        {
            long localNow = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            long offset = timer.TimerSystemCurrent - timer.TimerWhen;
            state.TimerStartTime = localNow - offset;
        }
        else
        {
            state.TimerStartTime = timer.TimerWhen;
        }
    }
}
