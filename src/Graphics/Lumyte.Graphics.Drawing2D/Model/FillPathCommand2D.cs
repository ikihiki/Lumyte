namespace Lumyte.Graphics.Drawing2D;

/// <summary>A recorded path fill, including implicitly closed open contours.</summary>
/// <param name="Path">The immutable contour snapshot.</param>
/// <param name="Paint">The paint evaluated in logical coordinates.</param>
/// <param name="Rule">The contour fill rule.</param>
/// <param name="State">The captured drawing state.</param>
public sealed record FillPathCommand2D(Path2D Path, PaintGraph2D Paint, FillRule2D Rule, DrawingState2D State) : DrawingCommand2D(State);
