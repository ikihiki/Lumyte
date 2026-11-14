using BenchmarkDotNet.Attributes;
using Lumyte.Memory.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;

namespace Lumyte.Memory.Benchmark;

/// <summary>Bounded ownership, ledger and dependency-injection workloads.</summary>
[BenchmarkCategory("Core", "Memory", "MemoryManagement")]
public class MemoryBenchmarks
{
    private static readonly MemoryPoolId _pool = new("benchmark");
    private MemoryManager _manager = null!;
    private IMemoryScope _scope = null!;

    /// <summary>Initializes the retained manager and scope.</summary>
    [GlobalSetup]
    public void Setup()
    {
        _manager = new MemoryManager();
        _scope = _manager.CreateScope("benchmark");
    }

    /// <summary>Disposes the retained owners in order.</summary>
    [GlobalCleanup]
    public void Cleanup()
    {
        _scope.DisposeAsync().GetAwaiter().GetResult();
        _manager.DisposeAsync().GetAwaiter().GetResult();
    }

    /// <summary>Constructs and releases a manager.</summary>
    /// <returns>The consumed operation result.</returns>
    [Benchmark]
    [BenchmarkCategory("CORE-MEM-01")]
    public async Task ManagerLifetimeAsync()
    {
        await using var manager = new MemoryManager();
    }

    /// <summary>Creates and releases an empty owned scope.</summary>
    /// <returns>The consumed operation result.</returns>
    [Benchmark]
    [BenchmarkCategory("CORE-MEM-01")]
    public async Task ScopeLifetimeAsync()
    {
        await using IMemoryScope scope = _manager.CreateScope("child");
    }

    /// <summary>Reads an existing pool snapshot.</summary>
    /// <returns>The consumed operation result.</returns>
    [Benchmark]
    [BenchmarkCategory("CORE-MEM-01")]
    public MemorySnapshot Snapshot() => _manager.GetSnapshot(_pool);

    /// <summary>Reserves, commits and releases an external logical charge.</summary>
    /// <returns>The consumed operation result.</returns>
    [Benchmark]
    [BenchmarkCategory("CORE-MEM-02")]
    public async Task ReservationAsync()
    {
        using IMemoryReservation reservation = await _scope.ReserveAsync(new MemoryRequest { Pool = _pool, Bytes = 4096, Purpose = MemoryPurpose.Build });
        using IMemoryCharge charge = reservation.Commit(new MemoryUsage(4096, MemoryAccuracy.Exact));
    }

    /// <summary>Disposes an uncommitted reservation.</summary>
    /// <returns>The consumed operation result.</returns>
    [Benchmark]
    [BenchmarkCategory("CORE-MEM-02")]
    public async Task AbandonReservationAsync()
    {
        using IMemoryReservation reservation = await _scope.ReserveAsync(new MemoryRequest { Pool = _pool, Bytes = 4096, Purpose = MemoryPurpose.Build });
    }

    /// <summary>Rents a first chunk and releases the final reference.</summary>
    /// <returns>The consumed operation result.</returns>
    [Benchmark]
    [BenchmarkCategory("CORE-MEM-03")]
    public async Task NewChunkAsync()
    {
        using IMemoryBuffer<byte> buffer = await _scope.RentAsync<byte>(_pool, 256, MemoryPurpose.Build);
        buffer.Memory.Span[0] = 17;
    }

    /// <summary>Releases a nonempty scope with 32 owned buffers.</summary>
    /// <returns>The consumed operation result.</returns>
    [Benchmark]
    [BenchmarkCategory("CORE-MEM-04")]
    public async Task PopulatedScopeLifetimeAsync()
    {
        await using IMemoryScope scope = _manager.CreateScope("populated");
        for (int i = 0; i < 32; i++)
        {
            await scope.RentAsync<byte>(_pool, 256, MemoryPurpose.Build);
        }
    }

    /// <summary>Registers and removes a borrowed reclaimer.</summary>
    [Benchmark]
    [BenchmarkCategory("CORE-MEM-05")]
    public void Registration()
    {
        using IDisposable registration = _manager.RegisterReclaimer(new ChargeReclaimer(null));
    }

    /// <summary>Requests real release of a freshly replenished charge.</summary>
    /// <returns>The consumed operation result.</returns>
    [Benchmark]
    [BenchmarkCategory("CORE-MEM-05")]
    public async Task<ulong> ReclaimAsync()
    {
        using IMemoryReservation reservation = await _scope.ReserveAsync(new MemoryRequest { Pool = _pool, Bytes = 4096, Purpose = MemoryPurpose.Resident });
        using IMemoryCharge charge = reservation.Commit(new MemoryUsage(4096, MemoryAccuracy.Exact));
        using IDisposable registration = _manager.RegisterReclaimer(new ChargeReclaimer(charge));
        return await _manager.ReclaimAsync(_pool, 4096);
    }

    /// <summary>Builds, resolves, borrows and disposes a complete service-provider lifetime.</summary>
    /// <returns>The consumed operation result.</returns>
    [Benchmark]
    [BenchmarkCategory("CORE-MEM-07")]
    public async Task ProviderLifetimeAsync()
    {
        var services = new ServiceCollection();
        services.AddMemoryManagement(options => options.SetBudget(_pool, 65536, 1048576));
        await using ServiceProvider provider = services.BuildServiceProvider();
        IMemoryManager manager = provider.GetRequiredService<IMemoryManager>();
        await using IMemoryScope scope = manager.CreateScope("consumer");
        using IMemoryBuffer<int> buffer = await scope.RentAsync<int>(_pool, 64, MemoryPurpose.Build);
        buffer.Memory.Span[0] = 123;
    }

    private sealed class ChargeReclaimer(IMemoryCharge? charge) : IMemoryReclaimer
    {
        public ValueTask<ulong> ReclaimAsync(MemoryPoolId pool, ulong targetBytes, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            charge?.Dispose();
            return ValueTask.FromResult(0UL);
        }
    }
}
