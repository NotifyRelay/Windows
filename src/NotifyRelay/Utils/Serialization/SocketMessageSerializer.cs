namespace NotifyRelay.Utils.Serialization;

public static class SocketMessageSerializer
{
    private static readonly JsonSerializerOptions options = new()
    {
        WriteIndented = false,
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public static string Serialize(object message) =>
        JsonSerializer.Serialize(message, options);

    public static T? Deserialize<T>(string json) =>
        JsonSerializer.Deserialize<T>(json, options);
}

