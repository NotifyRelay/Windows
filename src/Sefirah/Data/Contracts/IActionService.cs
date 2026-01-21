using NotifyRelay.Data.Models;

namespace NotifyRelay.Data.Contracts;

public interface IActionService
{
    /// <summary>
    /// Handle actions
    /// </summary>
    void HandleActionMessage(ActionMessage action);

    /// <summary>
    /// Initializes the service.
    /// </summary>
    Task InitializeAsync();
}
