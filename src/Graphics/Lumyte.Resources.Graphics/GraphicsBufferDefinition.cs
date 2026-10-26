using Lumyte.Graphics.Abstractions;

namespace Lumyte.Resources.Graphics;

/// <summary>Defines a shared typed allocation; initialized assets use a custom definition that completes uploads before publication.</summary>
/// <typeparam name="T">The unmanaged buffer element.</typeparam>
/// <param name="description">The immutable allocation settings.</param>
public sealed class GraphicsBufferDefinition<T>(BufferDesc<T> description) : GraphicsResourceDefinition<IGraphicsBuffer<T>>
    where T : unmanaged
{
    /// <inheritdoc/>
    public override ValueTask<IGraphicsBuffer<T>> CreateAsync(IGraphicDevice device, ResourceBuildContext context, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return new(device.CreateBuffer(description));
    }
}
