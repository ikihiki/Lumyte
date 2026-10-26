namespace Lumyte.Graphics.Drawing2D;

/// <summary>A recorded FillRoundedRect command.</summary>
/// <param name="Rect">The captured Rect value.</param>
/// <param name="Radius">The captured Radius value.</param>
/// <param name="Paint">The captured Paint value.</param>
/// <param name="State">The captured State value.</param>
public sealed record FillRoundedRectCommand2D(Rect2D Rect, float Radius, PaintGraph2D Paint, DrawingState2D State) : DrawingCommand2D(State);
