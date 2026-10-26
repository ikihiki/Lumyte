namespace Lumyte.Graphics.Drawing2D;

/// <summary>An immutable solid paint node.</summary>
/// <param name="Color">The Color value.</param>
public sealed record SolidPaintNode2D(Color2D Color) : PaintNode2D;
