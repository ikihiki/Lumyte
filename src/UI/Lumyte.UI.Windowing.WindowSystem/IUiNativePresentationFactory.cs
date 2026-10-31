using System.Numerics;
using Lumyte.Graphics.Drawing2D;
using Lumyte.Windowing.Abstractions;

namespace Lumyte.UI.Windowing.WindowSystem;

/// <summary>Creates drawing targets and retires GPU work, without owning the native window.</summary>
public interface IUiNativePresentationFactory
{
    /// <summary>Returns positive logical limits for the anchor's monitor work area.</summary>
    /// <param name="owner">The borrowed native owner.</param>
    /// <param name="anchor">The anchor in owner-client logical coordinates.</param>
    /// <returns>The maximum client size.</returns>
    Vector2 GetClientLimit(IWindow owner, Rect2D anchor);

    /// <summary>Prepares an initially hidden drawing target.</summary>
    /// <param name="window">The borrowed target.</param>
    /// <param name="view">The immutable drawing geometry.</param>
    /// <param name="surface">The borrowed UI content.</param>
    /// <param name="cancellationToken">Stops preparation and recovers partial resources.</param>
    /// <returns>A drawing token whose disposal retires GPU work.</returns>
    ValueTask<IUiWindowPresentation> PrepareAsync(IWindow window, UiWindowPresentationView view, UiSurface surface, CancellationToken cancellationToken);
}
