using System.Text.Json;
using System.Text.Json.Serialization;

namespace NotifyRelay.Worker.Bridge;

public class IpcMessage
{
    public string Type { get; set; } = string.Empty;
    public string? Id { get; set; }
    public string? Service { get; set; }
    public string? Method { get; set; }
    public string? EventName { get; set; }
    public JsonElement? Params { get; set; }
    public JsonElement? Data { get; set; }
    public bool? Success { get; set; }
    public Dictionary<string, object?>? Config { get; set; }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = false
    };

    public string Serialize() => JsonSerializer.Serialize(this, JsonOptions);

    public static IpcMessage? Deserialize(string json) =>
        JsonSerializer.Deserialize<IpcMessage>(json, JsonOptions);

    public static IpcMessage CreateEvent(string service, string eventName, object? data = null)
    {
        return new IpcMessage
        {
            Type = "event",
            Service = service,
            EventName = eventName,
            Data = data != null ? JsonSerializer.SerializeToElement(data, JsonOptions) : null
        };
    }

    public static IpcMessage CreateResponse(string id, bool success, object? data = null)
    {
        return new IpcMessage
        {
            Type = "response",
            Id = id,
            Success = success,
            Data = data != null ? JsonSerializer.SerializeToElement(data, JsonOptions) : null
        };
    }

    public static IpcMessage Shutdown() => new() { Type = "shutdown" };
}
