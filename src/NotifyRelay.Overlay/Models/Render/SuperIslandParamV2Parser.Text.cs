using System.Text.Json;

namespace NotifyRelay.Models.Render;

/// <summary>
/// SuperIslandParamV2Parser 的模板文本/图片域解析部分（baseInfo / chatInfo / highlightInfo /
/// hintInfo / picInfo / coverInfo / bgInfo / iconTextInfo / animTextInfo / textButton）。
/// </summary>
public static partial class SuperIslandParamV2Parser
{
    // ---------- templates 解析 ----------

    private static BaseInfoData? ParseBaseInfo(JsonElement root)
    {
        var bi = root.GetPropertyOrNull("baseInfo");
        if (bi == null) return null;
        var result = new BaseInfoData
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
            ColorTitleDark = GetString(bi, "colorTitleDark")?.TrimOrNull(),
            ColorSubTitle = GetString(bi, "colorSubTitle")?.TrimOrNull(),
            ColorSubTitleDark = GetString(bi, "colorSubTitleDark")?.TrimOrNull(),
            ColorExtraTitle = GetString(bi, "colorExtraTitle")?.TrimOrNull(),
            ColorExtraTitleDark = GetString(bi, "colorExtraTitleDark")?.TrimOrNull(),
            ColorSpecialTitle = GetString(bi, "colorSpecialTitle")?.TrimOrNull(),
            ColorSpecialTitleDark = GetString(bi, "colorSpecialTitleDark")?.TrimOrNull(),
            ColorSpecialBg = GetString(bi, "colorSpecialBg")?.TrimOrNull(),
            ColorSpecialBgDark = GetString(bi, "colorSpecialBgDark")?.TrimOrNull(),
            ColorContent = GetString(bi, "colorContent")?.TrimOrNull(),
            ColorContentDark = GetString(bi, "colorContentDark")?.TrimOrNull(),
            ColorSubContent = GetString(bi, "colorSubContent")?.TrimOrNull(),
            ColorSubContentDark = GetString(bi, "colorSubContentDark")?.TrimOrNull(),
            ShowDivider = GetBool(bi, "showDivider"),
            ShowContentDivider = GetBool(bi, "showContentDivider"),
        };
        if (result.Title == null && result.SubTitle == null && result.ExtraTitle == null
            && result.SpecialTitle == null && result.Content == null && result.SubContent == null)
        {
            return null;
        }
        return result;
    }

    private static ChatInfoData? ParseChatInfo(JsonElement root)
    {
        var ci = root.GetPropertyOrNull("chatInfo");
        if (ci == null) return null;
        var result = new ChatInfoData
        {
            PicProfile = GetString(ci, "picProfile")?.TrimOrNull(),
            PicProfileDark = GetString(ci, "picProfileDark")?.TrimOrNull(),
            AppIconPkg = GetString(ci, "appIconPkg")?.TrimOrNull(),
            Title = GetString(ci, "title")?.TrimOrNull(),
            Content = GetString(ci, "content")?.TrimOrNull(),
            TimerInfo = ParseTimerInfo(ci),
            ColorTitle = GetString(ci, "colorTitle")?.TrimOrNull(),
            ColorTitleDark = GetString(ci, "colorTitleDark")?.TrimOrNull(),
            ColorContent = GetString(ci, "colorContent")?.TrimOrNull(),
            ColorContentDark = GetString(ci, "colorContentDark")?.TrimOrNull(),
        };
        if (result.Title == null && result.Content == null && result.PicProfile == null
            && result.AppIconPkg == null && result.TimerInfo == null)
        {
            return null;
        }
        return result;
    }

    private static HighlightInfoData? ParseHighlightInfo(JsonElement root)
    {
        var hi = root.GetPropertyOrNull("highlightInfo");
        if (hi == null) return null;
        var result = new HighlightInfoData
        {
            Title = GetString(hi, "title")?.TrimOrNull(),
            TimerInfo = ParseTimerInfo(hi),
            Content = GetString(hi, "content")?.TrimOrNull(),
            PicFunction = GetString(hi, "picFunction")?.TrimOrNull(),
            PicFunctionDark = GetString(hi, "picFunctionDark")?.TrimOrNull(),
            SubContent = GetString(hi, "subContent")?.TrimOrNull(),
            Type = GetInt32(hi, "type"),
            ColorTitle = GetString(hi, "colorTitle")?.TrimOrNull(),
            ColorTitleDark = GetString(hi, "colorTitleDark")?.TrimOrNull(),
            ColorContent = GetString(hi, "colorContent")?.TrimOrNull(),
            ColorContentDark = GetString(hi, "colorContentDark")?.TrimOrNull(),
            ColorSubContent = GetString(hi, "colorSubContent")?.TrimOrNull(),
            ColorSubContentDark = GetString(hi, "colorSubContentDark")?.TrimOrNull(),
            BigImageLeft = GetString(hi, "bigImageLeft")?.TrimOrNull(),
            BigImageRight = GetString(hi, "bigImageRight")?.TrimOrNull(),
            IconOnly = GetBool(hi, "iconOnly"),
        };
        if (result.Title == null && result.Content == null && result.SubContent == null
            && result.PicFunction == null && result.PicFunctionDark == null
            && result.BigImageLeft == null && result.BigImageRight == null && result.TimerInfo == null)
        {
            return null;
        }
        return result;
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
            ColorTitleDark = GetString(hi, "colorTitleDark")?.TrimOrNull(),
            ColorSubTitle = GetString(hi, "colorSubTitle")?.TrimOrNull(),
            ColorSubTitleDark = GetString(hi, "colorSubTitleDark")?.TrimOrNull(),
            ColorContent = GetString(hi, "colorContent")?.TrimOrNull(),
            ColorContentDark = GetString(hi, "colorContentDark")?.TrimOrNull(),
            ColorSubContent = GetString(hi, "colorSubContent")?.TrimOrNull(),
            ColorSubContentDark = GetString(hi, "colorSubContentDark")?.TrimOrNull(),
            ColorContentBg = GetString(hi, "colorContentBg")?.TrimOrNull(),
            ColorContentBgDark = GetString(hi, "colorContentBgDark")?.TrimOrNull(),
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
            ColorTitleDark = GetString(pi, "colorTitleDark")?.TrimOrNull(),
        };
    }

    private static TextButtonData? ParseTextButton(JsonElement root)
    {
        if (!root.TryGetProperty("textButton", out var tb)) return null;

        // 模板约定：textButton 为 actionInfo 对象数组（1-2 项）
        if (tb.ValueKind == JsonValueKind.Array)
        {
            var actions = new List<ActionData>();
            foreach (var item in tb.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Object) continue;
                var action = ParseActionInfo(item);
                if (action != null) actions.Add(action);
            }
            return actions.Count > 0 ? new TextButtonData { Actions = actions } : null;
        }

        // 兼容旧结构：textButton 为对象且内含 actions 数组
        if (tb.ValueKind == JsonValueKind.Object)
        {
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

        return null;
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
            ColorTitleDark = GetString(ati, "colorTitleDark")?.TrimOrNull(),
            ColorContent = GetString(ati, "colorContent")?.TrimOrNull(),
            ColorContentDark = GetString(ati, "colorContentDark")?.TrimOrNull(),
        };
    }

    // ---------- 新增 OS3 组件解析 ----------

    /// <summary>解析新图文组件 iconTextInfo（图标 + 主/次文本）。</summary>
    private static IconTextInfoData? ParseIconTextInfo(JsonElement root)
    {
        var iti = root.GetPropertyOrNull("iconTextInfo");
        if (iti == null) return null;
        var icon = iti.Value.GetPropertyOrNull("animIconInfo");
        var title = GetString(iti, "title")?.TrimOrNull();
        var content = GetString(iti, "content")?.TrimOrNull();
        if (title == null && content == null) return null;
        return new IconTextInfoData
        {
            IconKey = GetString(icon, "src")?.TrimOrNull(),
            IconKeyDark = GetString(icon, "srcDark")?.TrimOrNull(),
            Title = title,
            Content = content,
            SubContent = GetString(iti, "subContent")?.TrimOrNull(),
            ColorTitle = GetString(iti, "colorTitle")?.TrimOrNull(),
            ColorTitleDark = GetString(iti, "colorTitleDark")?.TrimOrNull(),
            ColorContent = GetString(iti, "colorContent")?.TrimOrNull(),
            ColorContentDark = GetString(iti, "colorContentDark")?.TrimOrNull(),
        };
    }

    /// <summary>解析封面组件 coverInfo（封面图 + 主/次文本）。</summary>
    private static CoverInfoData? ParseCoverInfo(JsonElement root)
    {
        var ci = root.GetPropertyOrNull("coverInfo");
        if (ci == null) return null;
        var title = GetString(ci, "title")?.TrimOrNull();
        var content = GetString(ci, "content")?.TrimOrNull();
        var subContent = GetString(ci, "subContent")?.TrimOrNull();
        if (title == null && content == null && subContent == null) return null;
        return new CoverInfoData
        {
            PicCover = GetString(ci, "picCover")?.TrimOrNull(),
            Title = title,
            Content = content,
            SubContent = subContent,
            ColorTitle = GetString(ci, "colorTitle")?.TrimOrNull(),
            ColorTitleDark = GetString(ci, "colorTitleDark")?.TrimOrNull(),
            ColorContent = GetString(ci, "colorContent")?.TrimOrNull(),
            ColorContentDark = GetString(ci, "colorContentDark")?.TrimOrNull(),
            ColorSubContent = GetString(ci, "colorSubContent")?.TrimOrNull(),
            ColorSubContentDark = GetString(ci, "colorSubContentDark")?.TrimOrNull(),
        };
    }

    /// <summary>解析模板背景 bgInfo（type 1 全屏 / 2 右侧；picBg / colorBg）。</summary>
    private static BgInfoData? ParseBgInfo(JsonElement root)
    {
        var bg = root.GetPropertyOrNull("bgInfo");
        if (bg == null) return null;
        var pic = GetString(bg, "picBg")?.TrimOrNull();
        var color = GetString(bg, "colorBg")?.TrimOrNull();
        if (pic == null && color == null) return null;
        return new BgInfoData
        {
            Type = bg.Value.GetInt32OrDefault("type", 1),
            PicBg = pic,
            ColorBg = color,
        };
    }
}
