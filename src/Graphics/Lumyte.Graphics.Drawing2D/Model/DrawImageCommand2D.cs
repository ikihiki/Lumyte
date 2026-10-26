namespace Lumyte.Graphics.Drawing2D;

/// <summary>A recorded DrawImage command.</summary>
/// <param name="Image">The captured Image value.</param>
/// <param name="Source">The captured Source value.</param>
/// <param name="Destination">The captured Destination value.</param>
/// <param name="State">The captured State value.</param>
public sealed record DrawImageCommand2D(Image2D Image, Rect2D Source, Rect2D Destination, DrawingState2D State) : DrawingCommand2D(State);
