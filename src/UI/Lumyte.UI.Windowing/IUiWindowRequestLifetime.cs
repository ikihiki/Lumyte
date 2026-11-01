namespace Lumyte.UI.Windowing;

/// <summary>Optionally releases retained terminal operation results before their window is released.</summary>
public interface IUiWindowRequestLifetime
{
    /// <summary>Forgets a completed result; pending operations are never canceled or removed.</summary>
    /// <param name="request">A request belonging to this manager.</param>
    /// <returns>Whether a terminal result was removed. Subsequent queries for that handle return false.</returns>
    bool ReleaseRequest(UiWindowRequestId request);
}
