using NotifyRelay.Data.Enums;

namespace NotifyRelay.Data.Models;

public class DiscoveredDevice(
    string deviceId,
    string? publicKey,
    string deviceName,
    DateTimeOffset lastSeen,
    DeviceOrigin origin,
    int port)
{
    public string DeviceId { get; } = deviceId;
    public string? PublicKey { get; } = publicKey;
    public string DeviceName { get; } = deviceName;
    public DateTimeOffset LastSeen { get; } = lastSeen;
    public DeviceOrigin Origin { get; } = origin;
    public int Port { get; } = port;
}

