using Lumyte.Graphics.Abstractions;

namespace Lumyte.Resources.Graphics;

/// <summary>Defines a shared IGraphicsTexture allocation from immutable CPU settings.</summary>
/// <param name="description">The allocation settings; texture contents require a custom initialization definition.</param>
public sealed class GraphicsTextureDefinition(TextureDesc description) : GraphicsResourceDefinition<IGraphicsTexture>
{
    /// <inheritdoc/>
    public override ValueTask<IGraphicsTexture> CreateAsync(IGraphicDevice device, ResourceBuildContext context, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return new(device.CreateTexture(description));
    }
}
