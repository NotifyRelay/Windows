using NotifyRelay.Data.Models;

namespace NotifyRelay.Data.Contracts;

public interface IClipboardService
{
    /// <summary>
    /// Sets the content of the clipboard.
    /// </summary>
    Task SetContentAsync(object content, PairedDevice sourceDevice);
}
