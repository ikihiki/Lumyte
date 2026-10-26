namespace Lumyte.Graphics.Drawing2D;

/// <summary>A recorded FillRect command.</summary>
/// <param name="Rect">The captured Rect value.</param>
/// <param name="Paint">The captured Paint value.</param>
/// <param name="State">The captured State value.</param>
public sealed record FillRectCommand2D(Rect2D Rect, PaintGraph2D Paint, DrawingState2D State) : DrawingCommand2D(State);
