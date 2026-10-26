using Lumyte.Graphics.Abstractions;
using Lumyte.Graphics.ImageEffects;

namespace Lumyte.Resources.Graphics.ImageEffects;

/// <summary>Builds one coherent CPU shader pair from embedded defaults or tracked artifact dependencies.</summary>
/// <param name="package">The optional injected shader package; omission selects embedded defaults.</param>
public sealed class ImageEffectArtifactStage(ImageEffectShaderPackage? package = null) : IResourceSourceStage<ImageEffectShaders>
{
    private readonly ImageEffectShaderPackage _package = package ?? ImageEffectShaderPackage.Default;

    /// <inheritdoc/>
    public async ValueTask<ImageEffectShaders> BuildAsync(ResourceBuildContext context, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (_package.Sources is not { } sources)
        {
            return ImageEffectShaders.Embedded;
        }

        ShaderArtifact fullscreen = await context.RequireAsync(sources.Fullscreen, cancellationToken: cancellationToken);
        ShaderArtifact effects = await context.RequireAsync(sources.Effects, cancellationToken: cancellationToken);
        return new(fullscreen, effects);
    }

    /// <inheritdoc/>
    public ValueTask ReleaseAsync(ImageEffectShaders resource) => ValueTask.CompletedTask;
}
