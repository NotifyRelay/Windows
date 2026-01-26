using System.Net;

namespace NotifyRelay.Services.Socket;

public interface IUdpClientProvider
{
    void OnReceived(EndPoint endpoint, byte[] buffer, long offset, long size);
    void OnDisconnected();
}
