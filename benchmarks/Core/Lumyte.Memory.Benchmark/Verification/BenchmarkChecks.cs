namespace Lumyte.Memory.Benchmark;

/// <summary>Memory ownership and ledger correctness gates.</summary>
public static class BenchmarkChecks
{
    /// <summary>Checks actual release counts, bounded chunks, provider ownership and budget rejection.</summary>
    public static void Verify()
    {
        var benchmark = new MemoryBenchmarks();
        benchmark.Setup();
        try
        {
            benchmark.ReservationAsync().GetAwaiter().GetResult();
            benchmark.AbandonReservationAsync().GetAwaiter().GetResult();
            benchmark.NewChunkAsync().GetAwaiter().GetResult();
            benchmark.PopulatedScopeLifetimeAsync().GetAwaiter().GetResult();
            benchmark.ProviderLifetimeAsync().GetAwaiter().GetResult();
            if (benchmark.ReclaimAsync().GetAwaiter().GetResult() != 4096 || benchmark.Snapshot().CommittedBytes != 0 || benchmark.Snapshot().ReservedBytes != 0)
            {
                throw new InvalidOperationException("Ledger release mismatch.");
            }
        }
        finally
        {
            benchmark.Cleanup();
        }

        var chunks = new ChunkHitBenchmarks();
        for (int wave = 0; wave < 2; wave++)
        {
            chunks.Setup();
            try
            {
                if (chunks.HitWave() != 32)
                {
                    throw new InvalidOperationException("Finite wave length mismatch.");
                }

                chunks.VerifyWave();
            }
            finally
            {
                chunks.Cleanup();
            }
        }

        var contention = new ContentionBenchmarks { Workers = 2 };
        contention.Setup();
        try
        {
            if (contention.SharedBudgetWave() != 0 || contention.SharedBudgetWave() != 0)
            {
                throw new InvalidOperationException("Contended wave leaked committed bytes.");
            }
        }
        finally
        {
            contention.Cleanup();
        }

        var pool = new MemoryPoolId("bounded");
        var manager = new MemoryManager(new Dictionary<MemoryPoolId, MemoryBudget> { [pool] = new(4096, 4096) });
        IMemoryScope scope = manager.CreateScope("verify");
        using (IMemoryReservation reservation = scope.ReserveAsync(new MemoryRequest { Pool = pool, Bytes = 4096, Purpose = MemoryPurpose.Build }).GetAwaiter().GetResult())
        {
            try
            {
                scope.ReserveAsync(new MemoryRequest { Pool = pool, Bytes = 1, Purpose = MemoryPurpose.Build }).GetAwaiter().GetResult().Dispose();
                throw new InvalidOperationException("Hard budget accepted one excess byte.");
            }
            catch (MemoryBudgetExceededException)
            {
            }
        }

        scope.DisposeAsync().GetAwaiter().GetResult();
        manager.DisposeAsync().GetAwaiter().GetResult();
    }
}
