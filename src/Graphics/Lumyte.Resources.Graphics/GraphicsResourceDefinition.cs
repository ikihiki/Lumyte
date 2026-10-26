using Lumyte.Graphics.Abstractions;

namespace Lumyte.Resources.Graphics;

/// <summary>Describes how to reconstruct a shared GPU resource without capturing a physical device.</summary>
/// <typeparam name="T">The owned native output.</typeparam>
public abstract class GraphicsResourceDefinition<T>
    where T : class, IDisposable
{
    /// <summary>Creates a complete native value, ready for publication.</summary>
    /// <param name="device">The device selected for this construction.</param>
    /// <param name="context">Tracks dependencies, CPU inputs and partial cleanup.</param>
    /// <param name="cancellationToken">Cancels construction.</param>
    /// <returns>The transferred native value.</returns>
    /// <remarks>Retain GPU dependencies as Resident. Complete initialization uploads before returning. Register partial allocations for retryable cleanup before further fallible work.</remarks>
    public abstract ValueTask<T> CreateAsync(IGraphicDevice device, ResourceBuildContext context, CancellationToken cancellationToken);
}
