using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.Drawing2D;

/// <summary>Provides the device-independent embedded artifacts implementing the 2D drawing ABI.</summary>
public static class DrawingShaders2D
{
    private static readonly Lazy<ShaderArtifact> _vertex = new(static () => ShaderArtifact.LoadEmbedded(typeof(DrawingShaders2D).Assembly, "Lumyte.Drawing2D.drawing-vertex.lshader"));
    private static readonly Lazy<ShaderArtifact> _fragment = new(static () => ShaderArtifact.LoadEmbedded(typeof(DrawingShaders2D).Assembly, "Lumyte.Drawing2D.drawing-fragment.lshader"));

    /// <summary>Gets the embedded drawing vertex artifact.</summary>
    public static ShaderArtifact Vertex => _vertex.Value;

    /// <summary>Gets the embedded paint/coverage fragment artifact.</summary>
    public static ShaderArtifact Fragment => _fragment.Value;
}
