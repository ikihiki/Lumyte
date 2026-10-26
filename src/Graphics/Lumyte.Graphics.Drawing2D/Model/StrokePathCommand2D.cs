namespace Lumyte.Graphics.Drawing2D;

/// <summary>A recorded StrokePath command.</summary>
/// <param name="Path">The captured Path value.</param>
/// <param name="Paint">The captured Paint value.</param>
/// <param name="Style">The captured Style value.</param>
/// <param name="State">The captured State value.</param>
public sealed record StrokePathCommand2D(Path2D Path, PaintGraph2D Paint, StrokeStyle2D Style, DrawingState2D State) : DrawingCommand2D(State);
