namespace Lumyte.Graphics.Drawing2D;

/// <summary>A recorded DrawCoverageMask command.</summary>
/// <param name="Mask">The captured Mask value.</param>
/// <param name="Source">The captured Source value.</param>
/// <param name="Destination">The captured Destination value.</param>
/// <param name="Paint">The captured Paint value.</param>
/// <param name="State">The captured State value.</param>
public sealed record DrawCoverageMaskCommand2D(CoverageImage2D Mask, Rect2D Source, Rect2D Destination, PaintGraph2D Paint, DrawingState2D State) : DrawingCommand2D(State);
