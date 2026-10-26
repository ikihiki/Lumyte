using Lumyte.Graphics.Abstractions;

namespace Lumyte.Resources.Graphics;

/// <summary>Selects two tracked shader inputs for a logical program resource.</summary>
/// <param name="vertex">The source pipeline key for the vertex artifact.</param>
/// <param name="fragment">The source pipeline key for the fragment artifact.</param>
public sealed class GraphicsProgramPackage(ResourceKey<ShaderArtifact> vertex, ResourceKey<ShaderArtifact> fragment)
{
    /// <summary>Gets the tracked vertex artifact key.</summary>
    public ResourceKey<ShaderArtifact> Vertex { get; } = vertex;

    /// <summary>Gets the tracked fragment artifact key.</summary>
    public ResourceKey<ShaderArtifact> Fragment { get; } = fragment;
}
