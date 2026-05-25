using System.Security.Cryptography;
using System.Text;

namespace NotifyRelay.Services;

public static class SuperIslandProtocol
{
    public const string FeatureKeyName = "si_feature_id";
    public const string TerminateValue = "__END__";

    public static string ComputeFeatureId(
        string? superPkg,
        string? paramV2Raw,
        string? title,
        string? text,
        string? instanceId = null)
    {
        var keyParts = new List<string>
        {
            superPkg ?? string.Empty
        };

        if (!string.IsNullOrWhiteSpace(paramV2Raw))
        {
            try
            {
                using var doc = JsonDocument.Parse(paramV2Raw);
                var root = doc.RootElement;

                if (root.TryGetProperty("chatInfo", out var chatInfo) && chatInfo.ValueKind == JsonValueKind.Object)
                {
                    var t = GetString(chatInfo, "title");
                    if (!string.IsNullOrWhiteSpace(t)) keyParts.Add("chat:" + t);
                }
                else if (root.TryGetProperty("baseInfo", out var baseInfo) && baseInfo.ValueKind == JsonValueKind.Object)
                {
                    var t = GetString(baseInfo, "title");
                    var c = GetString(baseInfo, "content");
                    if (!string.IsNullOrWhiteSpace(t)) keyParts.Add("baseT:" + t);
                    if (!string.IsNullOrWhiteSpace(c)) keyParts.Add("baseC:" + c);
                }
                else if (root.TryGetProperty("highlightInfo", out var highlight) && highlight.ValueKind == JsonValueKind.Object)
                {
                    var t = GetString(highlight, "title");
                    if (!string.IsNullOrWhiteSpace(t)) keyParts.Add("hi:" + t);
                }
            }
            catch { }
        }

        if (keyParts.Count <= 1)
        {
            if (!string.IsNullOrWhiteSpace(title)) keyParts.Add("t:" + title);
            if (!string.IsNullOrWhiteSpace(text)) keyParts.Add("c:" + text);
        }

        if (!string.IsNullOrWhiteSpace(instanceId))
        {
            keyParts.Add("id:" + instanceId);
        }

        var raw = string.Join("|", keyParts);
        return Sha1(raw);
    }

    private static string? GetString(JsonElement obj, string propertyName)
    {
        if (obj.TryGetProperty(propertyName, out var prop) && prop.ValueKind == JsonValueKind.String)
        {
            return prop.GetString();
        }
        return null;
    }

    private static string Sha1(string input)
    {
        using var sha1 = SHA1.Create();
        var bytes = sha1.ComputeHash(Encoding.UTF8.GetBytes(input));
        var sb = new StringBuilder(bytes.Length * 2);
        foreach (var b in bytes)
        {
            sb.Append(b.ToString("x2"));
        }
        return sb.ToString();
    }
}
