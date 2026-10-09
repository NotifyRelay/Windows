namespace NotifyRelay.Utils.Json;

/// <summary>
/// <see cref="JsonElement"/> 的取值扩展。
/// </summary>
public static class JsonElementExtensions
{
    /// <summary>
    /// 取字符串属性：属性存在且 <see cref="JsonValueKind.String"/> 时返回其值，否则返回 null。
    /// </summary>
    public static string? TryGetString(this JsonElement element, string propertyName)
    {
        if (element.TryGetProperty(propertyName, out var prop) && prop.ValueKind == JsonValueKind.String)
        {
            return prop.GetString();
        }
        return null;
    }
}