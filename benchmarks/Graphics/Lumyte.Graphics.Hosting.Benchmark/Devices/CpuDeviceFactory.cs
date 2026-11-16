using Lumyte.Graphics.Benchmarks.Testing;

namespace Lumyte.Graphics.Hosting.Benchmark.Devices;

/// <summary>Creates real managed storage/recording devices rather than an empty backend.</summary>
public sealed class CpuDeviceFactory : IGraphicsDeviceFactory
{
    /// <inheritdoc/>
    public ValueTask<IGraphicsDeviceOwner> CreateAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult<IGraphicsDeviceOwner>(new CpuGraphicsDevice());
    }
}
