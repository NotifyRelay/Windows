using NotifyRelay.Data.Models.Actions;

namespace NotifyRelay.Data.Contracts;

public interface IActionDialog
{
    /// <summary>
    /// Shows a dialog to create or edit the action.
    /// </summary>
    /// <param name="xamlRoot">The XamlRoot for the dialog.</param>
    /// <returns>The created/edited action, or null if cancelled.</returns>
    Task<BaseAction?> ShowDialogAsync(XamlRoot xamlRoot);
}
