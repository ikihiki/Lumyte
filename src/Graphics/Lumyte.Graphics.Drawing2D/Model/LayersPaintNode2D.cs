namespace Lumyte.Graphics.Drawing2D;

/// <summary>An immutable layers paint node.</summary>
/// <param name="Children">The Children value.</param>
public sealed record LayersPaintNode2D(IReadOnlyList<PaintNode2D> Children) : PaintNode2D;
