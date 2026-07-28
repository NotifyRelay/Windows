using System.Collections.Concurrent;
using Microsoft.Windows.ApplicationModel.Resources;

namespace NotifyRelay.Extensions;

/// <summary>
/// Extension methods for working with localized resources and message formatting.
/// </summary>
public static class StringExtensions
{
    private static readonly ConcurrentDictionary<string, string> cachedResources = new();
    private static readonly ResourceManager resourceManager = new();

    /// <summary>
    /// Retrieves a localized resource string from the resource map.
    /// </summary>
    /// <param name="resourceKey">The key for the resource string.</param>
    /// <returns>The localized resource string.</returns>
    public static string GetLocalizedResource(this string resourceKey)
    {
        if (cachedResources.TryGetValue(resourceKey, out var value))
        {
            return value;
        }

        // MRT Core stores dotted resw keys (e.g. "A.B") as nested paths, lookup requires '/'.
        // Use TryGetValue so missing keys don't throw (ResourceLoader.GetString throws COMException).
        var candidate = resourceManager.MainResourceMap.TryGetValue($"Resources/{resourceKey.Replace('.', '/')}");
        value = candidate?.ValueAsString;
        if (string.IsNullOrEmpty(value))
        {
            // Resource not found, fall back to the key itself.
            return resourceKey;
        }

        cachedResources.TryAdd(resourceKey, value);
        return value;
    }

    /// <summary>
    /// Checks if the string is a valid Base64 encoded string.
    /// </summary>
    /// <param name="input">The string to check.</param>
    /// <returns>True if the string is a valid Base64 encoded string, otherwise false.</returns>
    public static bool IsBase64String(this string input)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return false;
        }

        try
        {
            // Check for valid Base64 length (must be a multiple of 4)
            if (input.Length % 4 != 0)
            {
                return false;
            }

            // Try to decode the string
            Convert.FromBase64String(input);
            return true;
        }
        catch
        {
            return false;
        }
    }
}
