namespace Lumyte.UI.Windowing;

/// <summary>Supports atomic presentation replacement independently of navigation.</summary>
public interface IUiWindowPresentationStager
{
    /// <summary>Creates a tracked staging token without publishing or acquiring resources.</summary>
    /// <param name="current">The current value.</param>
    /// <param name="view">The view value.</param>
    /// <param name="candidate">The candidate value.</param>
    /// <returns>The result or asynchronous completion.</returns>
    IUiStagedWindowPresentation BeginReplacement(IUiWindowPresentation? current, UiWindowPresentationView view, UiSurface candidate);
}
