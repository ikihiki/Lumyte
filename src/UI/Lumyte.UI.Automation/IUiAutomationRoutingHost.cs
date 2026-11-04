using System.Numerics;

namespace Lumyte.UI.Automation;

/// <summary>Optional routing through window occlusion and menu keyboard scopes; called inside RunOnUiThreadAsync.</summary>
public interface IUiAutomationRoutingHost
{
    /// <summary>Probes the actual routed pointer destination without side effects.</summary>
    /// <param name="surface">The host's borrowed surface.</param>
    /// <param name="logicalPosition">Surface-local position.</param>
    /// <returns>A probe only when that surface is the actual destination.</returns>
    UiPointerProbe ProbePointer(UiSurface surface, Vector2 logicalPosition);

    /// <summary>Tests whether this surface owns the current keyboard scope.</summary>
    /// <param name="surface">The host's borrowed surface.</param>
    /// <returns>Whether key input may reach that surface.</returns>
    bool CanReceiveKeys(UiSurface surface);

    /// <summary>Dispatches exactly once through the host's shared input router.</summary>
    /// <param name="surface">The host's borrowed surface.</param>
    /// <param name="input">Surface-local synthetic input, including cleanup cancellation.</param>
    /// <returns>Whether the router consumed input.</returns>
    bool Dispatch(UiSurface surface, UiInput input);
}
