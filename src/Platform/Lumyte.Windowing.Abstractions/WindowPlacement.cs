namespace Lumyte.Windowing.Abstractions;

/// <summary>Defines WindowPlacement.</summary>
/// <param name="Anchor">The Anchor value.</param>
/// <param name="PreferredSide">The PreferredSide value.</param>
/// <param name="Gap">The Gap value.</param>
public sealed record WindowPlacement(
    WindowRect Anchor, WindowPlacementSide PreferredSide, float Gap = 0);
