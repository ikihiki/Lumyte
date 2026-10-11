namespace Lumyte.Graphics.Drawing2D;

/// <summary>A recorded DrawDistanceField command.</summary>
/// <param name="Field">The captured Field value.</param>
/// <param name="Source">The captured Source value.</param>
/// <param name="Destination">The captured Destination value.</param>
/// <param name="Paint">The captured Paint value.</param>
/// <param name="State">The captured State value.</param>
public sealed record DrawDistanceFieldCommand2D(DistanceField2D Field, Rect2D Source, Rect2D Destination, PaintGraph2D Paint, DrawingState2D State) : DrawingCommand2D(State);
