namespace NotifyRelay.Data.Contracts;

/// <summary>
/// Manages system-wide media playback monitoring and control.
/// Provides functionality to track active media sessions, handle playback changes,
/// and control media playback across different applications.
/// </summary>
public interface IPlaybackService
{
    Task InitializeAsync();

    /// <summary>
    /// Executes the corresponding media control action on the current device.
    /// </summary>
    /// <param name="mediaActionJson">The media action JSON payload.</param>
    Task HandleMediaActionAsync(string mediaActionJson);

    /// <summary>
    /// Handles a media playback message from the remote device.
    /// </summary>
    /// <param name="data">The playback data JSON payload.</param>
    Task HandleRemotePlaybackMessageAsync(string data);

    /// <summary>
    /// Sends a media control request to the specified device.
    /// </summary>
    /// <param name="deviceId">The target device ID.</param>
    /// <param name="controlType">The control type (e.g., play, pause, next).</param>
    void SendMediaControlRequest(string deviceId, string controlType);
}

