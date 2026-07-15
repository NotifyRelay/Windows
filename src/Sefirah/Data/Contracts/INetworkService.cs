using NotifyRelay.Data.Models;

namespace NotifyRelay.Data.Contracts;

public interface INetworkService
{
    Task<bool> StartServerAsync();
    int ServerPort { get; }
    void SendMessage(string deviceId, string message);
    void UpdateDeviceStatusFromUdp(string deviceId, string? message = null);

}
