namespace Lumyte.Graphics.Drawing2D;

/// <summary>An immutable clip paint node.</summary>
/// <param name="Path">The Path value.</param>
/// <param name="Rule">The Rule value.</param>
/// <param name="Child">The Child value.</param>
public sealed record ClipPaintNode2D(Path2D Path, FillRule2D Rule, PaintNode2D Child) : PaintNode2D;
