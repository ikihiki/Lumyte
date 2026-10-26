using Lumyte.Graphics.Abstractions;

namespace Lumyte.Resources.Graphics;

/// <summary>Defines a shared view retaining its texture as a resident dependency.</summary>
/// <param name="texture">The source texture resource.</param>
/// <param name="description">The optional immutable view settings.</param>
public sealed class GraphicsTextureViewDefinition(ResourceKey<GraphicsResource<IGraphicsTexture>> texture, TextureViewDesc? description = null) : GraphicsResourceDefinition<IGraphicsTextureView>
{
    /// <inheritdoc/>
    public override async ValueTask<IGraphicsTextureView> CreateAsync(IGraphicDevice device, ResourceBuildContext context, CancellationToken cancellationToken)
    {
        IGraphicsTexture source = await GraphicsResourceDependency.RequireAsync(texture, device, context, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        return source.CreateView(description);
    }
}
