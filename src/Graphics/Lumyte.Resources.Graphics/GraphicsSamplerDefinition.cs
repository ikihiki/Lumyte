using Lumyte.Graphics.Abstractions;

namespace Lumyte.Resources.Graphics;

/// <summary>Defines a shared IGraphicsSampler allocation from immutable CPU settings.</summary>
/// <param name="description">The allocation settings; texture contents require a custom initialization definition.</param>
public sealed class GraphicsSamplerDefinition(SamplerDesc description) : GraphicsResourceDefinition<IGraphicsSampler>
{
    /// <inheritdoc/>
    public override ValueTask<IGraphicsSampler> CreateAsync(IGraphicDevice device, ResourceBuildContext context, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return new(device.CreateSampler(description));
    }
}
