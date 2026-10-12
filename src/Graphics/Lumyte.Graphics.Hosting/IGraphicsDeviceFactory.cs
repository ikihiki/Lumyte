namespace Lumyte.Graphics.Hosting;

/// <summary>Creates owned devices using backend configuration injected into the factory.</summary>
public interface IGraphicsDeviceFactory
{
    /// <summary>Transfers an initialized device owner to the caller.</summary>
    /// <param name="cancellationToken">Cancels creation; the factory cleans up partial allocations.</param>
    /// <returns>The initialized owner.</returns>
    ValueTask<IGraphicsDeviceOwner> CreateAsync(CancellationToken cancellationToken = default);
}
