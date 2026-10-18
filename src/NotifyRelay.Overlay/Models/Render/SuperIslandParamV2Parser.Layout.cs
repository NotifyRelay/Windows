using System.Text.Json;

namespace NotifyRelay.Models.Render;

/// <summary>
/// SuperIslandParamV2Parser 的进度/计时/动作解析与 param_island（A/B 区）布局解析部分。
/// </summary>
public static partial class SuperIslandParamV2Parser
{
    // ---------- components 解析 ----------

    private static ProgressData? ParseProgressInfo(JsonElement parent)
    {
        var pi = parent.GetPropertyOrNull("progressInfo");
        if (pi == null) return null;
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

    private static MultiProgressData? ParseMultiProgressInfo(JsonElement root)
    {
        var mpi = root.GetPropertyOrNull("multiProgressInfo");
        if (mpi == null) return null;
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
            ActionTitleColorDark = GetString(ai, "actionTitleColorDark")?.TrimOrNull(),
            ActionBgColor = GetString(ai, "actionBgColor")?.TrimOrNull(),
            ActionBgColorDark = GetString(ai, "actionBgColorDark")?.TrimOrNull(),
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

    /// <summary>解析按钮组件5 highlightInfoV3（高亮文本 + 标签 + 圆头按钮）。</summary>
    private static HighlightInfoV3Data? ParseHighlightInfoV3(JsonElement root)
    {
        var v3 = root.GetPropertyOrNull("highlightInfoV3");
        if (v3 == null) return null;
        var action = v3.Value.GetPropertyOrNull("actionInfo");
        var primary = GetString(v3, "primaryText")?.TrimOrNull();
        if (primary == null && action == null) return null;
        return new HighlightInfoV3Data
        {
            PrimaryText = primary,
            SecondaryText = GetString(v3, "secondaryText")?.TrimOrNull(),
            ShowSecondaryLine = GetBool(v3, "showSecondaryLine"),
            HighLightText = GetString(v3, "highLightText")?.TrimOrNull(),
            PrimaryColor = GetString(v3, "primaryColor")?.TrimOrNull(),
            SecondaryColor = GetString(v3, "secondaryColor")?.TrimOrNull(),
            HighLightTextColor = GetString(v3, "highLightTextColor")?.TrimOrNull(),
            HighLightBgColor = GetString(v3, "highLightbgColor")?.TrimOrNull(),
            PrimaryColorDark = GetString(v3, "primaryColorDark")?.TrimOrNull(),
            SecondaryColorDark = GetString(v3, "secondaryColorDark")?.TrimOrNull(),
            HighLightTextColorDark = GetString(v3, "highLightTextColorDark")?.TrimOrNull(),
            HighLightBgColorDark = GetString(v3, "highLightbgColorDark")?.TrimOrNull(),
            ActionInfo = ParseActionInfo(v3),
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
}
