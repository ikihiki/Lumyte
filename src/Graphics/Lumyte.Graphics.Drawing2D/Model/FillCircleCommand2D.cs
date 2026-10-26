using System.Numerics;

namespace Lumyte.Graphics.Drawing2D;

/// <summary>A recorded FillCircle command.</summary>
/// <param name="Center">The captured Center value.</param>
/// <param name="Radius">The captured Radius value.</param>
/// <param name="Paint">The captured Paint value.</param>
/// <param name="State">The captured State value.</param>
public sealed record FillCircleCommand2D(Vector2 Center, float Radius, PaintGraph2D Paint, DrawingState2D State) : DrawingCommand2D(State);
