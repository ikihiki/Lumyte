namespace Lumyte.Graphics.Drawing2D;

/// <summary>A recorded FillPath command.</summary>
/// <param name="Path">The captured Path value.</param>
/// <param name="Paint">The captured Paint value.</param>
/// <param name="Rule">The captured Rule value.</param>
/// <param name="State">The captured State value.</param>
public sealed record FillPathCommand2D(Path2D Path, PaintGraph2D Paint, FillRule2D Rule, DrawingState2D State) : DrawingCommand2D(State);
