using System.Text.Json;

namespace NotifyRelay.Models.Render;

/// <summary>
/// SuperIslandParamV2Parser 的 JsonElement 便捷访问与通用文本提取部分。
/// </summary>
public static partial class SuperIslandParamV2Parser
{
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
