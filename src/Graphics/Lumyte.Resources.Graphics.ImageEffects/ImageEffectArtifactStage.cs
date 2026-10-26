using Lumyte.Graphics.ImageEffects;

namespace Lumyte.Resources.Graphics.ImageEffects;

/// <summary>Builds a coherent CPU shader pair from embedded defaults or shared typed shader configuration.</summary>
/// <param name="package">The optional inputs registered with UseGraphicsShaders for ImageEffectShaderResource.</param>
public sealed class ImageEffectArtifactStage(GraphicsShaderPackage<ImageEffectShaderResource>? package = null) : IResourceSourceStage<ImageEffectShaders>
{
    /// <inheritdoc/>
    public async ValueTask<ImageEffectShaders> BuildAsync(ResourceBuildContext context, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (package is null)
        {
            return ImageEffectShaders.Embedded;
        }

        GraphicsProgramArtifacts artifacts = await package.ResolveAsync(context, cancellationToken);
        return new(artifacts.Vertex, artifacts.Fragment);
    }

    /// <inheritdoc/>
    public ValueTask ReleaseAsync(ImageEffectShaders resource) => ValueTask.CompletedTask;
}
