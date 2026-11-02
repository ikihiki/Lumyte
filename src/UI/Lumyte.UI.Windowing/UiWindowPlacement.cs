using Lumyte.Graphics.Drawing2D;

namespace Lumyte.UI.Windowing;

/// <summary>A detached UiWindowPlacement value.</summary>
/// <param name="AnchorWindow">The AnchorWindow value.</param>
/// <param name="Anchor">The Anchor value.</param>
/// <param name="PreferredSide">The PreferredSide value.</param>
/// <param name="Gap">The Gap value.</param>
public sealed record UiWindowPlacement(UiWindowId AnchorWindow, Rect2D Anchor, UiWindowPlacementSide PreferredSide, float Gap = 0)
{
    /// <summary>Gets a value indicating whether vertical placement aligns to the anchor right edge.</summary>
    public bool AlignEnd { get; init; }
}
