using Lumyte.Graphics.Abstractions;
using Lumyte.Graphics.Drawing2D;

namespace Lumyte.Resources.Graphics.Drawing2D;

/// <summary>Resolves the 2D shader inputs selected at application composition.</summary>
/// <param name="package">The optional DI shader inputs; omission selects embedded artifacts.</param>
public sealed class Renderer2DArtifactStage(GraphicsShaderPackage<Renderer2DResource>? package = null) : IResourceSourceStage<Renderer2DArtifacts>
{
    private static readonly Lazy<Renderer2DArtifacts> _embedded = new(static () => new(new(
        ShaderArtifact.LoadEmbedded(typeof(DrawingPrograms2D).Assembly, "Lumyte.Drawing2D.drawing-vertex.lshader"),
        ShaderArtifact.LoadEmbedded(typeof(DrawingPrograms2D).Assembly, "Lumyte.Drawing2D.drawing-fragment.lshader"))));

    /// <inheritdoc/>
    public async ValueTask<Renderer2DArtifacts> BuildAsync(ResourceBuildContext context, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return package is null ? _embedded.Value : new(await package.ResolveAsync(context, cancellationToken));
    }

    /// <inheritdoc/>
    public ValueTask ReleaseAsync(Renderer2DArtifacts resource) => ValueTask.CompletedTask;
}
