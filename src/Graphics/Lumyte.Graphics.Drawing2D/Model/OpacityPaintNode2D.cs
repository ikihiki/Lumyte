namespace Lumyte.Graphics.Drawing2D;

/// <summary>An immutable opacity paint node.</summary>
/// <param name="Opacity">The Opacity value.</param>
/// <param name="Child">The Child value.</param>
public sealed record OpacityPaintNode2D(float Opacity, PaintNode2D Child) : PaintNode2D;
