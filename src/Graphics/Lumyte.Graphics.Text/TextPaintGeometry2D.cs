using Lumyte.Graphics.Drawing2D;

namespace Lumyte.Graphics.Text;

/// <summary>A bounded vector paint graph, including isolated COLR composites, in paragraph coordinates.</summary>
/// <param name="Paint">The immutable graph evaluated by the ordinary 2D backend.</param>
/// <param name="Bounds">The finite conservative painted bounds.</param>
public sealed record TextPaintGeometry2D(PaintGraph2D Paint, Rect2D Bounds) : TextDrawing2D;
