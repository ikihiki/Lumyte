using System.Numerics;

namespace Lumyte.UI.Automation;

/// <summary>Defines a pointer activation position and shared deadline.</summary>
public sealed record UiClickOptions : UiWaitOptions
{
    /// <summary>Gets an optional finite nonnegative offset from Bounds; omission chooses the clipped center.</summary>
    public Vector2? Position { get; init; }
}
