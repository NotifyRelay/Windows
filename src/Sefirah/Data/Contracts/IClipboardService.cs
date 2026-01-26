using NotifyRelay.Data.Models;

namespace NotifyRelay.Data.Contracts;

public interface IClipboardService
{
    /// <summary>
    /// Sets the content of the clipboard.
    /// </summary>
    Task SetContentAsync(object content, PairedDevice sourceDevice);

    /// <summary>
    /// 处理剪贴板消息 (DATA_CLIPBOARD)
    /// </summary>
    Task ProcessClipboardMessageAsync(PairedDevice device, string payload);
}
