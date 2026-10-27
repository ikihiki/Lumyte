using Lumyte.Graphics.Drawing2D;

namespace Lumyte.Graphics.Text;

/// <summary>An immutable bounded COLR paint graph, independent of its source font and native resources.</summary>
/// <param name="Paint">The vector paint graph in logical coordinates.</param>
/// <param name="Bounds">A finite conservative drawing rectangle in the same coordinates.</param>
public sealed record ColorGlyphPaint2D(PaintGraph2D Paint, Rect2D Bounds);
