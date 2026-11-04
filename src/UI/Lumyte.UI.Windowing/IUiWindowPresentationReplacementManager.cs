namespace Lumyte.UI.Windowing;

/// <summary>Supports atomic presentation replacement independently of navigation.</summary>
public interface IUiWindowPresentationReplacementManager
{
    /// <summary>Creates a tracked staging token without publishing or acquiring resources.</summary>
    /// <param name="window">The window value.</param>
    /// <param name="current">The current value.</param>
    /// <param name="candidate">The candidate value.</param>
    /// <returns>The result or asynchronous completion.</returns>
    IUiWindowPresentationReplacement BeginReplacement(UiWindowId window, UiWindowBinding? current, UiSurface candidate);
}
