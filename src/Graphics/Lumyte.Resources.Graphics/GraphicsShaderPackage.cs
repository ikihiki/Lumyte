using Lumyte.Graphics.Abstractions;

namespace Lumyte.Resources.Graphics;

/// <summary>Selects a tracked vertex/fragment artifact pair for one typed graphics consumer.</summary>
/// <typeparam name="TConsumer">The resource type consuming this shader ABI.</typeparam>
/// <param name="vertex">The vertex artifact key.</param>
/// <param name="fragment">The fragment artifact key.</param>
public sealed class GraphicsShaderPackage<TConsumer>(ResourceKey<ShaderArtifact> vertex, ResourceKey<ShaderArtifact> fragment)
{
    /// <summary>Gets the vertex artifact key.</summary>
    public ResourceKey<ShaderArtifact> Vertex { get; } = vertex;

    /// <summary>Gets the fragment artifact key.</summary>
    public ResourceKey<ShaderArtifact> Fragment { get; } = fragment;

}
