using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.Drawing2D;

/// <summary>Identifies the drawing evaluator's fixed argument, instruction and compositing ABI.</summary>
public static class DrawingPrograms2D
{
    /// <summary>The compatible drawing vertex, DrawingArguments and DrawingInstruction ABI.</summary>
    public const string ProgramId = "Lumyte.Drawing2D.paint-coverage.v1";

    /// <summary>Gets the stateless embedded provider; each acquisition owns fresh native objects.</summary>
    public static IGraphicsProgramProvider DefaultProvider { get; } = new EmbeddedGraphicsProgramProvider(typeof(DrawingPrograms2D).Assembly, ProgramId, "Lumyte.Drawing2D.drawing-vertex.lshader", "Lumyte.Drawing2D.drawing-fragment.lshader");
}
