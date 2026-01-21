using NotifyRelay.Data.Models;

namespace NotifyRelay.Data.Contracts;
public interface IMessageHandler
{
    Task HandleMessageAsync(PairedDevice device, SocketMessage message);
    void SendSftpCommand(PairedDevice device, string action, string? username = null, string? password = null);
}
