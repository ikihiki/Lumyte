using BenchmarkDotNet.Attributes;

namespace Lumyte.Memory.Benchmark;

/// <summary>Exercises finite hard-budget rejection and estimated external accounting.</summary>
[BenchmarkCategory("Standalone", "Core", "Memory", "Budgets", "SteadyState", "Screening", "Comparison", "Diagnostic")]
public class BudgetBenchmarks
{
    private readonly MemoryPoolId _pool = new("bounded-budget");
    private MemoryManager _manager = null!;
    private IMemoryScope _scope = null!;

    /// <summary>Configures an independently owned limited pool.</summary>
    [GlobalSetup]
    public void Setup()
    {
        _manager = new MemoryManager(new Dictionary<MemoryPoolId, MemoryBudget> { [_pool] = new(2048, 4096) });
        _scope = _manager.CreateScope("budget");
    }

    /// <summary>Gets the current pool ledger for deterministic verification.</summary>
    /// <returns>The current immutable snapshot.</returns>
    public MemorySnapshot Inspect() => _manager.GetSnapshot(_pool);

    /// <summary>Rejects a full-budget overflow, releases capacity and successfully reserves again.</summary>
    /// <returns>The number of actual budget exceptions observed.</returns>
    [Benchmark]
    public async Task<int> RejectAndRecoverAsync()
    {
        int rejected = 0;
        using (IMemoryReservation full = await Reserve(4096))
        {
            try
            {
                using IMemoryReservation unexpected = await Reserve(1);
            }
            catch (MemoryBudgetExceededException)
            {
                rejected++;
            }
        }

        using IMemoryReservation recovered = await Reserve(4096);
        return rejected;
    }

    /// <summary>Commits a partial estimated charge and reads the actual estimated ledger.</summary>
    /// <returns>The committed estimated byte count before release.</returns>
    [Benchmark]
    public async Task<ulong> EstimatedChargeAsync()
    {
        using IMemoryReservation reservation = await Reserve(4096);
        using IMemoryCharge charge = reservation.Commit(new MemoryUsage(1024, MemoryAccuracy.Estimate));
        MemorySnapshot snapshot = Inspect();
        if (snapshot.ReservedBytes != 0 || snapshot.CommittedBytes != 1024)
        {
            throw new InvalidOperationException("Partial estimated commit left incorrect accounting.");
        }

        return snapshot.EstimatedBytes;
    }

    /// <summary>Releases all budget owners after completion.</summary>
    [GlobalCleanup]
    public void Cleanup()
    {
        _scope.DisposeAsync().GetAwaiter().GetResult();
        _manager.DisposeAsync().GetAwaiter().GetResult();
    }

    private ValueTask<IMemoryReservation> Reserve(ulong bytes)
        => _scope.ReserveAsync(new MemoryRequest { Pool = _pool, Bytes = bytes, Purpose = MemoryPurpose.Resident });
}
