namespace Lumyte.Resources.Graphics;

/// <summary>Builds one CPU shader pair from constructor-injected application configuration.</summary>
/// <param name="package">The inputs registered with UseGraphicsShaders for GraphicsProgramResource.</param>
public sealed class GraphicsProgramArtifactStage(GraphicsShaderPackage<GraphicsProgramResource> package) : IResourceSourceStage<GraphicsProgramArtifacts>
{
    /// <inheritdoc/>
    public ValueTask<GraphicsProgramArtifacts> BuildAsync(ResourceBuildContext context, CancellationToken cancellationToken)
        => package.ResolveAsync(context, cancellationToken);

    /// <inheritdoc/>
    public ValueTask ReleaseAsync(GraphicsProgramArtifacts resource) => ValueTask.CompletedTask;
}
