using System.Numerics;

namespace Lumyte.Graphics.Drawing2D;

/// <summary>A recorded DrawLine command.</summary>
/// <param name="Start">The captured Start value.</param>
/// <param name="End">The captured End value.</param>
/// <param name="Paint">The captured Paint value.</param>
/// <param name="Style">The captured Style value.</param>
/// <param name="State">The captured State value.</param>
public sealed record DrawLineCommand2D(Vector2 Start, Vector2 End, PaintGraph2D Paint, StrokeStyle2D Style, DrawingState2D State) : DrawingCommand2D(State);
