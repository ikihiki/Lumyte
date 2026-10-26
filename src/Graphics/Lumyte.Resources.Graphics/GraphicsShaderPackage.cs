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

    /// <summary>Resolves a coherent artifact pair and retains both as resident dependencies.</summary>
    /// <param name="context">The consumer's resource build context.</param>
    /// <param name="cancellationToken">Cancels dependency preparation.</param>
    /// <returns>The retained CPU artifacts.</returns>
    public async ValueTask<GraphicsProgramArtifacts> ResolveAsync(ResourceBuildContext context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        ShaderArtifact vertex = await context.RequireAsync(Vertex, cancellationToken: cancellationToken);
        ShaderArtifact fragment = await context.RequireAsync(Fragment, cancellationToken: cancellationToken);
        return new(vertex, fragment);
    }
}
