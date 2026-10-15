namespace Lumyte.Graphics.Drawing2D;

/// <summary>An immutable image paint node.</summary>
/// <param name="Pattern">The Pattern value.</param>
public sealed record ImagePaintNode2D(ImagePattern2D Pattern) : PaintNode2D;
