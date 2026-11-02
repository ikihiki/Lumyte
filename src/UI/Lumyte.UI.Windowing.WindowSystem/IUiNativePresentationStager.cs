using Lumyte.Windowing.Abstractions;

namespace Lumyte.UI.Windowing.WindowSystem;

/// <summary>Optionally stages a replacement inside an existing native presentation target.</summary>
public interface IUiNativePresentationStager
{
    /// <summary>Reserves a private candidate without allocating native or GPU resources.</summary>
    /// <param name="window">The borrowed existing native window.</param>
    /// <param name="current">The current drawing registration.</param>
    /// <param name="view">The same target geometry.</param>
    /// <param name="candidate">The borrowed candidate surface.</param>
    /// <returns>A tracked staging token.</returns>
    IUiStagedWindowPresentation BeginReplacement(IWindow window, IUiWindowPresentation? current, UiWindowPresentationView view, UiSurface candidate);
}
