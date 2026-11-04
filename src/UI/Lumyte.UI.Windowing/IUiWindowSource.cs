using System.Numerics;

namespace Lumyte.UI.Windowing;

/// <summary>A borrowed presentation root, with explicit acquisition origin and immutable observations.</summary>
public interface IUiWindowSource
{
    /// <summary>Gets the nonreused connection identity.</summary>
    Guid SourceId { get; }

    /// <summary>Copies current geometry, activity and input origin.</summary>
    /// <returns>The current state.</returns>
    UiWindowSourceSnapshot CaptureState();

    /// <summary>Prepares a hidden drawing binding; cancellation must recover partial resources.</summary>
    /// <param name="view">Presentation geometry and layer.</param>
    /// <param name="surface">Borrowed content.</param>
    /// <param name="cancellationToken">Stops preparation.</param>
    /// <returns>A token whose asynchronous disposal retires GPU work.</returns>
    ValueTask<IUiWindowPresentation> PreparePresentationAsync(UiWindowPresentationView view, UiSurface surface, CancellationToken cancellationToken);

    /// <summary>Transforms between actual presentation sources without guessing.</summary>
    /// <param name="destination">The destination root.</param>
    /// <param name="position">Source logical position.</param>
    /// <param name="transformed">Destination logical position.</param>
    /// <returns>Whether conversion is supported.</returns>
    bool TryTransformPoint(IUiWindowSource destination, Vector2 position, out Vector2 transformed);
}
