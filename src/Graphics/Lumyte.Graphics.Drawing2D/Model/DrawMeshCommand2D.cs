namespace Lumyte.Graphics.Drawing2D;

/// <summary>A recorded DrawMesh command.</summary>
/// <param name="Mesh">The captured Mesh value.</param>
/// <param name="Paint">The captured Paint value.</param>
/// <param name="State">The captured State value.</param>
public sealed record DrawMeshCommand2D(Mesh2D Mesh, PaintGraph2D Paint, DrawingState2D State) : DrawingCommand2D(State);
