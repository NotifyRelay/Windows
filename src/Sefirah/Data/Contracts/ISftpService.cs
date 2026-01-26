using NotifyRelay.Data.Models;

namespace NotifyRelay.Data.Contracts;
public interface IftpService
{
    /// <summary>
    /// Initializes the ftp service with the server information and shell services.
    /// </summary>
    Task InitializeAsync(PairedDevice device, ftpServerInfo info);

    /// <summary>
    /// Removes the sync root.
    /// </summary>
    void Remove(string deviceId);
}
