using Lumyte.Graphics.Abstractions;

namespace Lumyte.Resources.Graphics;

/// <summary>Builds one CPU shader pair with resident dependencies on both source artifacts.</summary>
/// <param name="packages">The frozen shader input configuration.</param>
public sealed class GraphicsProgramArtifactStage(GraphicsProgramPackages packages) : IResourceSourceStage<GraphicsProgramArtifacts>
{
    /// <inheritdoc/>
    public async ValueTask<GraphicsProgramArtifacts> BuildAsync(ResourceBuildContext context, CancellationToken cancellationToken)
    {
        GraphicsProgramPackage package = packages.Get(context.Address);
        ShaderArtifact vertex = await context.RequireAsync(package.Vertex, cancellationToken: cancellationToken);
        ShaderArtifact fragment = await context.RequireAsync(package.Fragment, cancellationToken: cancellationToken);
        return new(vertex, fragment);
    }

    /// <inheritdoc/>
    public ValueTask ReleaseAsync(GraphicsProgramArtifacts resource) => ValueTask.CompletedTask;
}
