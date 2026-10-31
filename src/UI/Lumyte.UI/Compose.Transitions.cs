using System.Numerics;
using Lumyte.Core.Time;

namespace Lumyte.UI;

/// <summary>Provides generated static factories for retained UI widgets.</summary>
public static partial class Compose
{
    /// <summary>Defines an entry fade.</summary>
    /// <param name="duration">The interpolation interval.</param>
    /// <param name="delay">The initial hold interval.</param>
    /// <returns>The immutable effect.</returns>
    public static UiVisualTransition FadeIn(Duration duration, Duration delay = default) => new(new() { Opacity = 0 }, new(), duration, delay);

    /// <summary>Defines an exit fade.</summary>
    /// <param name="duration">The interpolation interval.</param>
    /// <param name="delay">The initial hold interval.</param>
    /// <returns>The immutable effect.</returns>
    public static UiVisualTransition FadeOut(Duration duration, Duration delay = default) => new(new(), new() { Opacity = 0 }, duration, delay);

    /// <summary>Defines an entry translation.</summary>
    /// <param name="offset">The starting logical offset.</param>
    /// <param name="duration">The interpolation interval.</param>
    /// <param name="delay">The initial hold interval.</param>
    /// <returns>The immutable effect.</returns>
    public static UiVisualTransition SlideIn(Vector2 offset, Duration duration, Duration delay = default) => new(new() { Offset = offset }, new(), duration, delay);

    /// <summary>Defines an exit translation.</summary>
    /// <param name="offset">The final logical offset.</param>
    /// <param name="duration">The interpolation interval.</param>
    /// <param name="delay">The initial hold interval.</param>
    /// <returns>The immutable effect.</returns>
    public static UiVisualTransition SlideOut(Vector2 offset, Duration duration, Duration delay = default) => new(new(), new() { Offset = offset }, duration, delay);
}
