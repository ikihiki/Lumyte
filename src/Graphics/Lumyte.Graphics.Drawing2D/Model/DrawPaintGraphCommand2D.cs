namespace Lumyte.Graphics.Drawing2D;

/// <summary>A recorded DrawPaintGraph command.</summary>
/// <param name="Graph">The captured Graph value.</param>
/// <param name="State">The captured State value.</param>
public sealed record DrawPaintGraphCommand2D(PaintGraph2D Graph, DrawingState2D State) : DrawingCommand2D(State);
