namespace Lumyte.Graphics.Drawing2D;

/// <summary>An immutable composite paint node.</summary>
/// <param name="Source">The Source value.</param>
/// <param name="Backdrop">The Backdrop value.</param>
/// <param name="Mode">The Mode value.</param>
public sealed record CompositePaintNode2D(PaintNode2D Source, PaintNode2D Backdrop, CompositeMode2D Mode) : PaintNode2D;
