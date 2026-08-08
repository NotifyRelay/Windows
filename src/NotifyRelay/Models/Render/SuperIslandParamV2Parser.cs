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
            };

            // highlightInfo 缺失时尝试从 iconTextInfo 回退构造
            result.HighlightInfo = TryParse(() => ParseHighlightInfo(root))
                ?? TryParse(() => ParseHighlightFromIconText(root));

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

    // ---------- templates 解析 ----------

    private static BaseInfoData ParseBaseInfo(JsonElement root)
    {
        var bi = root.GetPropertyOrNull("baseInfo")!;
        return new BaseInfoData
        {
            Type = bi.GetInt32OrDefault("type", 1),
            Title = GetString(bi, "title")?.TrimOrNull(),
            SubTitle = GetString(bi, "subTitle")?.TrimOrNull(),
            ExtraTitle = GetString(bi, "extraTitle")?.TrimOrNull(),
            SpecialTitle = GetString(bi, "specialTitle")?.TrimOrNull(),
            Content = GetString(bi, "content")?.TrimOrNull(),
            SubContent = GetString(bi, "subContent")?.TrimOrNull(),
            PicFunction = GetString(bi, "picFunction")?.TrimOrNull(),
            PicFunctionDark = GetString(bi, "picFunctionDark")?.TrimOrNull(),
            ColorTitle = GetString(bi, "colorTitle")?.TrimOrNull(),
            ColorSubTitle = GetString(bi, "colorSubTitle")?.TrimOrNull(),
            ColorExtraTitle = GetString(bi, "colorExtraTitle")?.TrimOrNull(),
            ColorSpecialTitle = GetString(bi, "colorSpecialTitle")?.TrimOrNull(),
            ColorSpecialBg = GetString(bi, "colorSpecialBg")?.TrimOrNull(),
            ColorContent = GetString(bi, "colorContent")?.TrimOrNull(),
            ColorSubContent = GetString(bi, "colorSubContent")?.TrimOrNull(),
            ShowDivider = GetBool(bi, "showDivider"),
            ShowContentDivider = GetBool(bi, "showContentDivider"),
        };
    }

    private static ChatInfoData ParseChatInfo(JsonElement root)
    {
        var ci = root.GetPropertyOrNull("chatInfo")!;
        return new ChatInfoData
        {
            PicProfile = GetString(ci, "picProfile")?.TrimOrNull(),
            PicProfileDark = GetString(ci, "picProfileDark")?.TrimOrNull(),
            AppIconPkg = GetString(ci, "appIconPkg")?.TrimOrNull(),
            Title = GetString(ci, "title")?.TrimOrNull(),
            Content = GetString(ci, "content")?.TrimOrNull(),
            TimerInfo = ParseTimerInfo(ci),
            ColorTitle = GetString(ci, "colorTitle")?.TrimOrNull(),
            ColorContent = GetString(ci, "colorContent")?.TrimOrNull(),
        };
    }

    private static HighlightInfoData ParseHighlightInfo(JsonElement root)
    {
        var hi = root.GetPropertyOrNull("highlightInfo")!;
        return new HighlightInfoData
        {
            Title = GetString(hi, "title")?.TrimOrNull(),
            TimerInfo = ParseTimerInfo(hi),
            Content = GetString(hi, "content")?.TrimOrNull(),
            PicFunction = GetString(hi, "picFunction")?.TrimOrNull(),
            PicFunctionDark = GetString(hi, "picFunctionDark")?.TrimOrNull(),
            SubContent = GetString(hi, "subContent")?.TrimOrNull(),
            Type = GetInt32(hi, "type"),
            ColorTitle = GetString(hi, "colorTitle")?.TrimOrNull(),
            ColorContent = GetString(hi, "colorContent")?.TrimOrNull(),
            ColorSubContent = GetString(hi, "colorSubContent")?.TrimOrNull(),
            BigImageLeft = GetString(hi, "bigImageLeft")?.TrimOrNull(),
            BigImageRight = GetString(hi, "bigImageRight")?.TrimOrNull(),
            IconOnly = GetBool(hi, "iconOnly"),
        };
    }

    /// <summary>highlightInfo 缺失时从 iconTextInfo 回退构造。</summary>
    private static HighlightInfoData? ParseHighlightFromIconText(JsonElement root)
    {
        if (!root.TryGetProperty("iconTextInfo", out var iconText) || iconText.ValueKind != JsonValueKind.Object)
            return null;

        var title = GetString(iconText, "title")?.TrimOrNull();
        var content = GetString(iconText, "content")?.TrimOrNull();
        var sub = new[] { "subTitle", "tip", "desc", "description" }
            .Select(k => GetString(iconText, k)?.TrimOrNull())
            .FirstOrDefault(v => !string.IsNullOrEmpty(v));
        if (title == null && content == null && sub == null) return null;

        var animIcon = iconText.GetPropertyOrNull("animIconInfo");
        var iconKey = GetString(animIcon, "src")?.TrimOrNull();
        var iconKeyDark = GetString(animIcon, "srcDark")?.TrimOrNull();

        var paramIsland = root.GetPropertyOrNull("param_island") ?? root.GetPropertyOrNull("paramIsland") ?? root.GetPropertyOrNull("islandParam");
        var big = ParseBigIslandArea(paramIsland, null, null);

        return new HighlightInfoData
        {
            Title = title,
            Content = content,
            SubContent = sub,
            PicFunction = iconKey,
            PicFunctionDark = iconKeyDark,
            ColorTitle = GetString(iconText, "titleColor")?.TrimOrNull(),
            ColorContent = GetString(iconText, "contentColor")?.TrimOrNull(),
            ColorSubContent = GetString(iconText, "subtitleColor")?.TrimOrNull(),
            BigImageLeft = big?.LeftImage,
            BigImageRight = big?.RightImage,
            IconOnly = true,
        };
    }

    private static HintInfoData ParseHintInfo(JsonElement root)
    {
        var hi = root.GetPropertyOrNull("hintInfo")!;
        return new HintInfoData
        {
            Type = hi.GetInt32OrDefault("type", 1),
            Title = GetString(hi, "title")?.TrimOrNull(),
            TimerInfo = ParseTimerInfo(hi),
            SubTitle = GetString(hi, "subTitle")?.TrimOrNull(),
            Content = GetString(hi, "content")?.TrimOrNull(),
            SubContent = GetString(hi, "subContent")?.TrimOrNull(),
            PicContent = GetString(hi, "picContent")?.TrimOrNull(),
            ColorTitle = GetString(hi, "colorTitle")?.TrimOrNull(),
            ColorSubTitle = GetString(hi, "colorSubTitle")?.TrimOrNull(),
            ColorContent = GetString(hi, "colorContent")?.TrimOrNull(),
            ColorSubContent = GetString(hi, "colorSubContent")?.TrimOrNull(),
            ColorContentBg = GetString(hi, "colorContentBg")?.TrimOrNull(),
            ActionInfo = ParseActionInfo(hi),
        };
    }

    private static PicInfoData ParsePicInfo(JsonElement root)
    {
        var pi = root.GetPropertyOrNull("picInfo")!;
        return new PicInfoData
        {
            Type = pi.GetInt32OrDefault("type", 1),
            Pic = GetString(pi, "pic")?.TrimOrNull(),
            PicDark = GetString(pi, "picDark")?.TrimOrNull(),
            ActionInfo = ParseActionInfo(pi),
            Title = GetString(pi, "title")?.TrimOrNull(),
            ColorTitle = GetString(pi, "colorTitle")?.TrimOrNull(),
        };
    }

    // ---------- components 解析 ----------

    private static ProgressData ParseProgressInfo(JsonElement parent)
    {
        var pi = parent.GetPropertyOrNull("progressInfo")!;
        return new ProgressData
        {
            Progress = pi.GetInt32OrDefault("progress", 0),
            ColorProgress = GetString(pi, "colorProgress")?.TrimOrNull(),
            ColorProgressEnd = GetString(pi, "colorProgressEnd")?.TrimOrNull(),
            PicForward = GetString(pi, "picForward")?.TrimOrNull(),
            PicMiddle = GetString(pi, "picMiddle")?.TrimOrNull(),
            PicMiddleUnselected = GetString(pi, "picMiddleUnselected")?.TrimOrNull(),
            PicEnd = GetString(pi, "picEnd")?.TrimOrNull(),
            PicEndUnselected = GetString(pi, "picEndUnselected")?.TrimOrNull(),
            IsCCW = GetBool(pi, "isCCW"),
            IsAutoProgress = GetBool(pi, "isAutoProgress"),
        };
    }

    private static MultiProgressData ParseMultiProgressInfo(JsonElement root)
    {
        var mpi = root.GetPropertyOrNull("multiProgressInfo")!;
        var middleUnselected = GetString(mpi, "picMiddleUnselected")?.TrimOrNull()
            ?? GetString(mpi, "picMiddelUnselected")?.TrimOrNull(); // 兼容拼写
        var points = GetInt32(mpi, "points");
        return new MultiProgressData
        {
            Title = GetString(mpi, "title")?.TrimOrNull() ?? string.Empty,
            Progress = mpi.GetInt32OrDefault("progress", 0),
            Color = GetString(mpi, "color")?.TrimOrNull(),
            Points = points,
            PicForward = GetString(mpi, "picForward")?.TrimOrNull(),
            PicForwardWait = GetString(mpi, "picForwardWait")?.TrimOrNull(),
            PicForwardBox = GetString(mpi, "picForwardBox")?.TrimOrNull(),
            PicMiddle = GetString(mpi, "picMiddle")?.TrimOrNull(),
            PicMiddleUnselected = middleUnselected,
            PicEnd = GetString(mpi, "picEnd")?.TrimOrNull(),
            PicEndUnselected = GetString(mpi, "picEndUnselected")?.TrimOrNull(),
        };
    }

    private static TimerInfoData? ParseTimerInfo(JsonElement? parent)
    {
        if (parent is null || parent.Value.ValueKind != JsonValueKind.Object) return null;
        if (!parent.Value.TryGetProperty("timerInfo", out var ti) || ti.ValueKind != JsonValueKind.Object)
            return null;
        return new TimerInfoData
        {
            TimerType = ti.GetInt32OrDefault("timerType", 0),
            TimerWhen = ti.GetInt64OrDefault("timerWhen", 0),
            TimerTotal = ti.GetInt64OrDefault("timerTotal", 0),
            TimerSystemCurrent = ti.GetInt64OrDefault("timerSystemCurrent", 0),
        };
    }

    private static ActionData? ParseActionInfo(JsonElement? parent)
    {
        if (parent is null || parent.Value.ValueKind != JsonValueKind.Object) return null;
        if (!parent.Value.TryGetProperty("actionInfo", out var ai) || ai.ValueKind != JsonValueKind.Object)
            return null;
        var intentType = ai.GetInt32OrDefault("actionIntentType", -1);
        var type = ai.GetInt32OrDefault("type", -1);
        return new ActionData
        {
            Action = GetString(ai, "action")?.TrimOrNull(),
            ActionIcon = GetString(ai, "actionIcon")?.TrimOrNull(),
            ActionIconDark = GetString(ai, "actionIconDark")?.TrimOrNull(),
            ActionTitle = GetString(ai, "actionTitle")?.TrimOrNull(),
            ActionTitleColor = GetString(ai, "actionTitleColor")?.TrimOrNull(),
            ActionBgColor = GetString(ai, "actionBgColor")?.TrimOrNull(),
            ActionIntentType = intentType >= 0 ? intentType : null,
            ActionIntent = GetString(ai, "actionIntent")?.TrimOrNull(),
            ClickWithCollapse = ai.TryGetProperty("clickWithCollapse", out var cwc) && cwc.ValueKind == JsonValueKind.True,
            Type = type >= 0 ? type : null,
            ProgressInfo = ai.TryGetProperty("progressInfo", out var pi) && pi.ValueKind == JsonValueKind.Object
                ? ParseProgressInfo(ai)
                : null,
        };
    }

    private static List<ActionData>? ParseActions(JsonElement root)
    {
        if (!root.TryGetProperty("actions", out var arr) || arr.ValueKind != JsonValueKind.Array)
            return null;
        var list = new List<ActionData>();
        foreach (var item in arr.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object) continue;
            var action = ParseActionInfo(item);
            if (action != null) list.Add(action);
        }
        return list.Count > 0 ? list : null;
    }

    private static TextButtonData? ParseTextButton(JsonElement root)
    {
        if (!root.TryGetProperty("textButton", out var tb) || tb.ValueKind != JsonValueKind.Object)
            return null;
        var actions = new List<ActionData>();
        if (tb.TryGetProperty("actions", out var arr) && arr.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in arr.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Object) continue;
                var action = ParseActionInfo(item);
                if (action != null) actions.Add(action);
            }
        }
        return actions.Count > 0 ? new TextButtonData { Actions = actions } : null;
    }

    private static AnimTextInfoData? ParseAnimTextInfo(JsonElement root)
    {
        if (!root.TryGetProperty("animTextInfo", out var ati) || ati.ValueKind != JsonValueKind.Object)
            return null;
        var icon = ati.GetPropertyOrNull("animIconInfo");
        var src = GetString(icon, "src")?.TrimOrNull();
        if (string.IsNullOrEmpty(src)) return null;

        var title = GetString(ati, "title")?.TrimOrNull();
        var timer = ParseTimerInfo(ati);
        if (title == null && timer == null) return null; // 至少二选一

        return new AnimTextInfoData
        {
            IconSrc = src,
            IconSrcDark = GetString(icon, "srcDark")?.TrimOrNull(),
            Title = title,
            Content = GetString(ati, "content")?.TrimOrNull(),
            TimerInfo = timer,
            ColorTitle = GetString(ati, "colorTitle")?.TrimOrNull(),
            ColorContent = GetString(ati, "colorContent")?.TrimOrNull(),
        };
    }

    // ---------- param_island / A-B 区解析 ----------

    private static ParamIslandData? ParseParamIsland(JsonElement root, string? picFunction, string? aodPic)
    {
        var island = root.GetPropertyOrNull("param_island") ?? root.GetPropertyOrNull("paramIsland") ?? root.GetPropertyOrNull("islandParam");
        if (island == null) return null;

        var small = island.GetPropertyOrNull("smallIslandArea") ?? island.GetPropertyOrNull("smallIsland");
        var smallArea = small == null ? null : ParseSmallIslandArea(small.Value);

        var bigJson = island.GetPropertyOrNull("bigIslandArea") ?? island.GetPropertyOrNull("bigIsland");
        var big = ParseBigIslandArea(bigJson, picFunction, aodPic);

        if (smallArea == null && big == null) return null;
        return new ParamIslandData { SmallIslandArea = smallArea, BigIslandArea = big };
    }

    private static SmallIslandAreaData? ParseSmallIslandArea(JsonElement obj)
    {
        var combine = obj.GetPropertyOrNull("combinePicInfo");
        var picInfo = combine?.GetPropertyOrNull("picInfo");
        var combineIcon = GetString(picInfo, "pic")?.TrimOrNull();
        var progress = combine?.GetPropertyOrNull("progressInfo");
        var progressInfo = progress == null ? null : ParseProgressInfo(combine!.Value);

        var icon = ExtractFirstString(obj, s_iconKeys) ?? ExtractNestedFirstString(obj, s_iconObjectKeys, s_iconKeys);

        return new SmallIslandAreaData
        {
            PrimaryText = ExtractFirstString(obj, s_primaryKeys) ?? ExtractNestedFirstString(obj, s_nestedTextKeys, s_primaryKeys),
            SecondaryText = ExtractFirstString(obj, s_secondaryKeys) ?? ExtractNestedFirstString(obj, s_nestedTextKeys, s_secondaryKeys),
            IconKey = combineIcon ?? icon,
            ProgressInfo = progressInfo,
        };
    }

    private static BigIslandAreaData? ParseBigIslandArea(JsonElement? json, string? picFunction, string? aodPic)
    {
        if (json == null || json.Value.ValueKind != JsonValueKind.Object) return null;

        var leftText = json.Value.GetPropertyOrNull("imageTextInfoLeft");
        var rightText = json.Value.GetPropertyOrNull("imageTextInfoRight");
        var leftPic = leftText?.GetPropertyOrNull("picInfo") is { } lp
            ? GetString(lp, "pic")?.TrimOrNull() : null;
        var rightPic = rightText?.GetPropertyOrNull("picInfo") is { } rp
            ? GetString(rp, "pic")?.TrimOrNull() : null;

        // 验证码识别
        bool isVerCode = false;
        string? verCode = null;
        var leftTextInfo = leftText?.GetPropertyOrNull("textInfo");
        if (leftTextInfo is { } lti)
        {
            var title = GetString(lti, "title");
            if (GetBool(lti, "showHighlightColor") && !string.IsNullOrEmpty(title) && title.Contains("验证码"))
            {
                isVerCode = true;
                verCode = GetString(json.Value.GetPropertyOrNull("textInfo"), "title");
            }
        }

        var primary = ExtractFirstString(json.Value, s_primaryKeys)
            ?? ExtractNestedFirstString(json.Value, s_nestedTextKeys, s_primaryKeys);
        var secondary = ExtractFirstString(json.Value, s_secondaryKeys)
            ?? ExtractNestedFirstString(json.Value, s_nestedTextKeys, s_secondaryKeys);

        if (isVerCode && string.IsNullOrWhiteSpace(verCode))
        {
            verCode = primary;
        }

        var aComponent = ParseAComponent(json.Value, picFunction, aodPic);
        var bComponent = ParseBComponent(json.Value, picFunction, aodPic);

        return new BigIslandAreaData
        {
            PrimaryText = primary,
            SecondaryText = secondary,
            LeftImage = leftPic,
            RightImage = rightPic,
            VerificationCode = verCode,
            IsVerificationCode = isVerCode,
            AComponent = aComponent,
            BComponent = bComponent,
        };
    }

    /// <summary>解析 A 区（imageTextInfoLeft）。type=1 → 图文组件1；type=5 → 图文组件5。</summary>
    private static AComponentData? ParseAComponent(JsonElement bigIsland, string? picFunction, string? aodPic)
    {
        var left = bigIsland.GetPropertyOrNull("imageTextInfoLeft");
        if (left == null) return null;
        var type = left.Value.GetInt32OrDefault("type", 0);
        var textInfo = left.Value.GetPropertyOrNull("textInfo");

        var title = GetString(left.Value, "title")?.TrimOrNull()
            ?? GetString(textInfo, "title")?.TrimOrNull();
        var content = GetString(left.Value, "content")?.TrimOrNull()
            ?? GetString(textInfo, "content")?.TrimOrNull();
        var narrowFont = GetBool(textInfo, "narrowFont");
        var showHighlightColor = GetBool(textInfo, "showHighlightColor");

        var picInfo = left.Value.GetPropertyOrNull("picInfo");
        var picType = picInfo?.GetInt32OrDefault("type", 0) ?? 0;
        var picRaw = GetString(picInfo, "pic")?.TrimOrNull();
        var picKey = ResolvePicKey(picRaw, picFunction, aodPic, null);

        switch (type)
        {
            case 1:
                if (picType == 4 && picKey == null) return null; // type=4 静态图标必须有效
                return new AImageText1Data { Title = title, Content = content, NarrowFont = narrowFont, ShowHighlightColor = showHighlightColor, PicKey = picKey };
            case 5:
                if (title == null || picType != 4 || picKey == null) return null;
                return new AImageText5Data { Title = title, Content = content, NarrowFont = narrowFont, ShowHighlightColor = showHighlightColor, PicKey = picKey };
            default:
                return null;
        }
    }

    /// <summary>解析 B 区：优先 imageTextInfoRight type，其次 text/digit/progress/pic，兜底 BEmpty。</summary>
    private static BComponentData? ParseBComponent(JsonElement bigIsland, string? picFunction, string? aodPic)
    {
        var right = bigIsland.GetPropertyOrNull("imageTextInfoRight");
        if (right != null)
        {
            var type = right.Value.GetInt32OrDefault("type", 0);
            var textInfo = right.Value.GetPropertyOrNull("textInfo");
            var title = GetString(right.Value, "title")?.TrimOrNull()
                ?? GetString(textInfo, "title")?.TrimOrNull();
            var content = GetString(right.Value, "content")?.TrimOrNull()
                ?? GetString(textInfo, "content")?.TrimOrNull();
            var frontTitle = GetString(textInfo, "frontTitle")?.TrimOrNull();
            var narrowFont = GetBool(textInfo, "narrowFont");
            var showHighlightColor = GetBool(textInfo, "showHighlightColor");

            var picInfo = right.Value.GetPropertyOrNull("picInfo");
            var picTypeOk = picInfo?.GetInt32OrDefault("type", 0) == 1;
            var picRaw = GetString(picInfo, "pic")?.TrimOrNull();
            var picKey = ResolvePicKey(picRaw, picFunction, aodPic, null);

            switch (type)
            {
                case 2:
                    if (title == null || !picTypeOk || picKey == null) return new BEmptyData();
                    return new BImageTextData { Kind = "imageText2", FrontTitle = frontTitle, Title = title, Content = content, NarrowFont = narrowFont, ShowHighlightColor = showHighlightColor, PicKey = picKey };
                case 3:
                    if (title == null || !picTypeOk || picKey == null) return new BEmptyData();
                    return new BImageTextData { Kind = "imageText3", Title = title, NarrowFont = narrowFont, ShowHighlightColor = showHighlightColor, PicKey = picKey };
                case 4: // 系统侧专用，不复刻
                    return new BEmptyData();
                case 6:
                    if (title == null || picInfo?.GetInt32OrDefault("type", 0) != 4 || picKey == null) return new BEmptyData();
                    return new BImageTextData { Kind = "imageText6", Title = title, NarrowFont = narrowFont, ShowHighlightColor = showHighlightColor, PicKey = picKey };
                default:
                    return new BEmptyData();
            }
        }

        // textInfo
        if (bigIsland.GetPropertyOrNull("textInfo") is { } ti)
        {
            var title = GetString(ti, "title")?.TrimOrNull();
            if (title == null) return new BEmptyData();
            return new BImageTextData
            {
                Kind = "textInfo",
                FrontTitle = GetString(ti, "frontTitle")?.TrimOrNull(),
                Title = title,
                Content = GetString(ti, "content")?.TrimOrNull(),
                NarrowFont = GetBool(ti, "narrowFont"),
                ShowHighlightColor = GetBool(ti, "showHighlightColor"),
            };
        }

        // fixedWidthDigitInfo
        if (bigIsland.GetPropertyOrNull("fixedWidthDigitInfo") is { } fi)
        {
            var digit = GetString(fi, "digit")?.TrimOrNull() ?? GetString(fi, "text")?.TrimOrNull();
            if (digit == null) return new BEmptyData();
            return new BDigitInfoData
            {
                Kind = "fixedWidthDigitInfo",
                Digit = digit,
                Content = GetString(fi, "content")?.TrimOrNull(),
                ShowHighlightColor = GetBool(fi, "showHighlightColor"),
            };
        }

        // sameWidthDigitInfo（timer 与 digit 二选一）
        if (bigIsland.GetPropertyOrNull("sameWidthDigitInfo") is { } si)
        {
            var timer = si.GetPropertyOrNull("timerInfo") is { }
                ? ParseTimerInfo(si)
                : null;
            var digit = GetString(si, "digit")?.TrimOrNull() ?? GetString(si, "text")?.TrimOrNull();
            if (timer == null && digit == null) return new BEmptyData();
            return new BDigitInfoData
            {
                Kind = "sameWidthDigitInfo",
                Digit = digit,
                Timer = timer,
                Content = GetString(si, "content")?.TrimOrNull(),
                ShowHighlightColor = GetBool(si, "showHighlightColor"),
            };
        }

        // progressTextInfo
        if (bigIsland.GetPropertyOrNull("progressTextInfo") is { } pt)
        {
            var ti2 = pt.GetPropertyOrNull("textInfo");
            var pInfo = pt.GetPropertyOrNull("progressInfo");
            var progress = pInfo?.GetInt32OrDefault("progress", -1) ?? -1;
            if (progress is < 0 or > 100) return new BEmptyData();

            var picObj = pt.GetPropertyOrNull("picInfo");
            var picRaw2 = picObj is { } po && po.GetInt32OrDefault("type", 0) == 1
                ? GetString(po, "pic")?.TrimOrNull() : null;
            var picKey2 = ResolvePicKey(picRaw2, picFunction, aodPic, null);

            return new BProgressTextInfoData
            {
                Kind = "progressTextInfo",
                FrontTitle = GetString(ti2, "frontTitle")?.TrimOrNull(),
                Title = GetString(ti2, "title")?.TrimOrNull(),
                Content = GetString(ti2, "content")?.TrimOrNull(),
                NarrowFont = GetBool(ti2, "narrowFont"),
                ShowHighlightColor = GetBool(ti2, "showHighlightColor"),
                Progress = progress,
                ColorReach = GetString(pInfo, "colorReach")?.TrimOrNull(),
                ColorUnReach = GetString(pInfo, "colorUnReach")?.TrimOrNull(),
                IsCCW = GetBool(pInfo, "isCCW"),
                PicKey = picKey2,
            };
        }

        // picInfo（type 1/4）
        if (bigIsland.GetPropertyOrNull("picInfo") is { } pi)
        {
            var type = pi.GetInt32OrDefault("type", -1);
            if (type is not (1 or 4)) return new BEmptyData();
            var picRaw = GetString(pi, "pic")?.TrimOrNull();
            if (picRaw == null) return new BEmptyData();
            var picKey = ResolvePicKey(picRaw, picFunction, aodPic, picRaw);
            if (picKey == null) return new BEmptyData();
            return new BPicInfoData { PicKey = picKey, Type = type };
        }

        return new BEmptyData();
    }

    /// <summary>
    /// 图标键解析优先级：picRaw(miui.focus.pic_ 前缀) &gt; picFunction &gt; aodPic；无匹配返回 default。
    /// </summary>
    private static string? ResolvePicKey(string? picRaw, string? picFunction, string? aodPic, string? @default)
    {
        if (picRaw != null && picRaw.StartsWith("miui.focus.pic_", StringComparison.Ordinal)) return picRaw;
        if (picFunction != null && picFunction.StartsWith("miui.focus.pic_", StringComparison.Ordinal)) return picFunction;
        if (aodPic != null && aodPic.StartsWith("miui.focus.pic_", StringComparison.Ordinal)) return aodPic;
        return @default;
    }

    // ---------- 通用提取 ----------

    private static readonly string[] s_primaryKeys = ["title", "primaryText", "frontTitle", "mainText", "mainTitle", "largeText", "bigText", "text"];
    private static readonly string[] s_secondaryKeys = ["content", "secondaryText", "subTitle", "subContent", "afterText", "tailText"];
    private static readonly string[] s_iconKeys = ["icon", "iconKey", "pic", "picContent", "picFunction", "picIcon", "picUrl", "src"];
    private static readonly string[] s_iconObjectKeys = ["iconInfo", "picInfo", "icon", "animIconInfo", "imageInfo", "imageIcon"];
    private static readonly string[] s_nestedTextKeys = ["textInfo", "leftTextInfo", "iconTextInfo", "imageTextInfoLeft", "imageTextInfoRight", "imageTextInfo", "smallTextInfo"];
    private static readonly string[] s_arrayKeys = ["components", "componentList", "items", "subItems"];

    private static string? ExtractFirstString(JsonElement obj, string[] keys)
    {
        foreach (var key in keys)
        {
            var value = GetString(obj, key)?.TrimOrNull();
            if (!string.IsNullOrEmpty(value)) return value;
        }
        return null;
    }

    private static string? ExtractNestedFirstString(JsonElement obj, string[] nestedKeys, string[] targetKeys)
    {
        foreach (var key in nestedKeys)
        {
            if (!obj.TryGetProperty(key, out var nested) || nested.ValueKind != JsonValueKind.Object) continue;
            var direct = ExtractFirstString(nested, targetKeys);
            if (!string.IsNullOrEmpty(direct)) return direct;
            foreach (var arrayKey in s_arrayKeys)
            {
                if (!nested.TryGetProperty(arrayKey, out var arr) || arr.ValueKind != JsonValueKind.Array) continue;
                foreach (var child in arr.EnumerateArray())
                {
                    if (child.ValueKind != JsonValueKind.Object) continue;
                    var found = ExtractNestedFirstString(child, nestedKeys, targetKeys);
                    if (!string.IsNullOrEmpty(found)) return found;
                }
            }
        }
        return null;
    }

    private static TimerInfoData? FirstTimerInfo(ParamV2 parsed)
    {
        return parsed.AnimTextInfo?.TimerInfo
            ?? parsed.HighlightInfo?.TimerInfo
            ?? parsed.ChatInfo?.TimerInfo
            ?? parsed.HintInfo?.TimerInfo;
    }

    private static void AppendInfoTexts(BaseInfoData? info, List<string> parts)
    {
        if (info == null) return;
        TryAppend(info.SpecialTitle, parts);
        TryAppend(info.Title, parts);
        TryAppend(info.SubTitle, parts);
        TryAppend(info.ExtraTitle, parts);
        TryAppend(info.Content, parts);
        TryAppend(info.SubContent, parts);
    }

    private static void AppendInfoTexts(ChatInfoData? info, List<string> parts)
    {
        if (info == null) return;
        TryAppend(info.Title, parts);
        TryAppend(info.Content, parts);
    }

    private static void AppendInfoTexts(HighlightInfoData? info, List<string> parts)
    {
        if (info == null) return;
        TryAppend(info.Title, parts);
        TryAppend(info.Content, parts);
        TryAppend(info.SubContent, parts);
    }

    private static void TryAppend(string? value, List<string> parts)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            parts.Add(value);
        }
    }

    /// <summary>
    /// 根据 ProgressInfo 构造多节点进度信息（节点资源存在时）。对应 Android ProgressInfo.toMultiProgressInfo。
    /// </summary>
    internal static MultiProgressData? ToMultiProgressInfo(this ProgressData? progressInfo, string? title)
    {
        if (progressInfo == null) return null;
        var hasNodeAssets = new[] { progressInfo.PicMiddle, progressInfo.PicMiddleUnselected, progressInfo.PicEnd, progressInfo.PicEndUnselected, progressInfo.PicForward }
            .Any(v => !string.IsNullOrWhiteSpace(v));
        if (!hasNodeAssets) return null;

        var resolvedColor = progressInfo.ColorProgress ?? progressInfo.ColorProgressEnd;
        return new MultiProgressData
        {
            Title = title?.Trim() ?? string.Empty,
            Progress = progressInfo.Progress,
            Color = resolvedColor,
            PicForward = progressInfo.PicForward,
            PicMiddle = progressInfo.PicMiddle,
            PicMiddleUnselected = progressInfo.PicMiddleUnselected,
            PicEnd = progressInfo.PicEnd,
            PicEndUnselected = progressInfo.PicEndUnselected,
        };
    }

    // ---------- JsonElement 便捷访问 ----------

    private static JsonElement? GetPropertyOrNull(this JsonElement element, string propertyName)
    {
        if (element.ValueKind != JsonValueKind.Object) return null;
        return element.TryGetProperty(propertyName, out var prop) ? prop : null;
    }

    private static JsonElement? GetPropertyOrNull(this JsonElement? element, string propertyName)
    {
        if (element is null || element.Value.ValueKind != JsonValueKind.Object) return null;
        return element.Value.TryGetProperty(propertyName, out var prop) ? prop : null;
    }

    private static string? GetString(JsonElement? element, string propertyName)
    {
        if (element is null || element.Value.ValueKind != JsonValueKind.Object) return null;
        if (element.Value.TryGetProperty(propertyName, out var prop) && prop.ValueKind == JsonValueKind.String)
        {
            return prop.GetString();
        }
        return null;
    }

    private static int? GetInt32(JsonElement? element, string propertyName)
    {
        if (element is null || element.Value.ValueKind != JsonValueKind.Object) return null;
        if (element.Value.TryGetProperty(propertyName, out var prop) && prop.ValueKind == JsonValueKind.Number)
        {
            return prop.GetInt32();
        }
        return null;
    }

    private static int GetInt32OrDefault(this JsonElement? element, string propertyName, int defaultValue = 0)
    {
        return GetInt32(element, propertyName) ?? defaultValue;
    }

    private static int GetInt32OrDefault(this JsonElement element, string propertyName, int defaultValue = 0)
    {
        return GetInt32(element, propertyName) ?? defaultValue;
    }

    private static long? GetInt64(JsonElement? element, string propertyName)
    {
        if (element is null || element.Value.ValueKind != JsonValueKind.Object) return null;
        if (element.Value.TryGetProperty(propertyName, out var prop) && prop.ValueKind == JsonValueKind.Number)
        {
            return prop.GetInt64();
        }
        return null;
    }

    private static long GetInt64OrDefault(this JsonElement? element, string propertyName, long defaultValue = 0)
    {
        return GetInt64(element, propertyName) ?? defaultValue;
    }

    private static long GetInt64OrDefault(this JsonElement element, string propertyName, long defaultValue = 0)
    {
        return GetInt64(element, propertyName) ?? defaultValue;
    }

    private static bool GetBool(JsonElement? element, string propertyName)
    {
        if (element is null || element.Value.ValueKind != JsonValueKind.Object) return false;
        if (element.Value.TryGetProperty(propertyName, out var prop) && prop.ValueKind == JsonValueKind.True)
        {
            return true;
        }
        return false;
    }

    private static T? TryParse<T>(Func<T?> func) where T : class
    {
        try
        {
            return func();
        }
        catch (JsonException)
        {
            return null;
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }

    private static string? TrimOrNull(this string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        return value.Trim();
    }
}
