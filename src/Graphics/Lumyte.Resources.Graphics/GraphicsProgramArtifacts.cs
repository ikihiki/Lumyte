using Lumyte.Graphics.Abstractions;

namespace Lumyte.Resources.Graphics;

/// <summary>A coherent CPU artifact pair, independent of the physical device epoch.</summary>
/// <param name="vertex">The compiled vertex artifact.</param>
/// <param name="fragment">The compiled fragment artifact.</param>
public sealed class GraphicsProgramArtifacts(ShaderArtifact vertex, ShaderArtifact fragment)
{
    /// <summary>Gets the retained vertex artifact.</summary>
    public ShaderArtifact Vertex { get; } = vertex ?? throw new ArgumentNullException(nameof(vertex));

    /// <summary>Gets the retained fragment artifact.</summary>
    public ShaderArtifact Fragment { get; } = fragment ?? throw new ArgumentNullException(nameof(fragment));
}
