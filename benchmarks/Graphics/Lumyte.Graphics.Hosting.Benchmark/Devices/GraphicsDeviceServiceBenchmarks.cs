using BenchmarkDotNet.Attributes;
using Lumyte.Graphics.Abstractions;
using Microsoft.Extensions.DependencyInjection;

namespace Lumyte.Graphics.Hosting.Benchmark.Devices;

/// <summary>Public DI-owned device service and exact epoch leases; no native initialization.</summary>
[MemoryDiagnoser]
[BenchmarkCategory("Screening", "Comparison", "Diagnostic")]
public class GraphicsDeviceServiceBenchmarks
{
    private ServiceProvider _provider = null!;
    private IGraphicsDeviceService _devices = null!;

    /// <summary>Registers and initializes one persistent service.</summary>
    [GlobalSetup]
    public void Setup()
    {
        _provider = CreateProvider();
        _devices = _provider.GetRequiredService<IGraphicsDeviceService>();
        _devices.InitializeAsync().GetAwaiter().GetResult();
    }

    /// <summary>Acquires an exact epoch, creates and releases real storage, returns the lease.</summary>
    /// <returns>The consumed value or operation observation.</returns>
    [Benchmark]
    public ulong AcquireUseRelease()
    {
        using GraphicsDeviceLease lease = _devices.AcquireAsync().GetAwaiter().GetResult();
        using IGraphicsBuffer<uint> buffer = lease.Device.CreateBuffer<uint>(new() { Count = 64, Usage = BufferUsage.CopySource, Memory = MemoryPreference.Upload });
        buffer.MapAsync().GetAwaiter().GetResult();
        buffer.CopyFrom([1u, 2u, 3u]);
        buffer.Unmap();
        return lease.State.Epoch;
    }

    /// <summary>Includes registration, resolution, initialization, real use, lease and owner disposal.</summary>
    /// <returns>The consumed value or operation observation.</returns>
    [Benchmark]
    public async Task<ulong> RegisterInitializeDisposeAsync()
    {
        await using ServiceProvider provider = CreateProvider();
        IGraphicsDeviceService devices = provider.GetRequiredService<IGraphicsDeviceService>();
        await devices.InitializeAsync();
        using GraphicsDeviceLease lease = await devices.AcquireAsync();
        using IGraphicsBuffer<uint> buffer = lease.Device.CreateBuffer<uint>(new() { Count = 16, Usage = BufferUsage.CopySource });
        return buffer.SizeInBytes + lease.State.Epoch;
    }

    /// <summary>Releases the retained owner.</summary>
    [GlobalCleanup]
    public void Cleanup() => _provider.DisposeAsync().AsTask().GetAwaiter().GetResult();

    private static ServiceProvider CreateProvider()
    {
        var services = new ServiceCollection();
        services.AddGraphicsDevice<CpuDeviceFactory>(ServiceLifetime.Singleton);
        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
    }
}
