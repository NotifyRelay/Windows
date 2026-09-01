using Microsoft.Extensions.Configuration;

namespace NotifyRelay.Data.Configuration;

/// <summary>
/// Helper to read and write strongly-typed settings through IConfigurationRoot,
/// persisting values as JSON strings (compatible with the legacy settings storage format).
/// </summary>
internal static class SettingsStorageHelper
{
    public static T? Get<T>(this IConfigurationRoot configuration, string key, T? defaultValue = default)
    {
        var raw = configuration[key];
        if (raw is null)
            return defaultValue;

        try
        {
            return JsonSerializer.Deserialize<T>(raw) ?? defaultValue;
        }
        catch
        {
            return defaultValue;
        }
    }

    public static bool Set<T>(this IConfigurationRoot configuration, string key, T? value)
    {
        var newJson = value is null ? null : JsonSerializer.Serialize(value);
        if (string.Equals(configuration[key], newJson, StringComparison.Ordinal))
            return false;

        configuration[key] = newJson;
        return true;
    }
}
