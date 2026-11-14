namespace Lumyte.Signals.Benchmark;

/// <summary>Checks existing and added signal workloads without timing.</summary>
public static class BenchmarkChecks
{
    /// <summary>Checks graph results, ownership and cancellation transitions.</summary>
    public static void Verify()
    {
        var scalar = new ScalarBenchmarks();
        Check(scalar.ReadValue() == 42 && scalar.ReadPeek() == 42);
        scalar.WriteSame();
        scalar.WriteUnobserved();
        var graph = new GraphBenchmarks();
        graph.Setup();
        try
        {
            Check(graph.DiamondUpdate() == 4 && graph.SwitchBranch() == 1 && graph.CheckReadSet() && graph.CaptureEightReads().IsCurrent);
        }
        finally
        {
            graph.Cleanup();
        }

        var asynchronous = new AsyncBenchmarks();
        asynchronous.Setup();
        try
        {
            Check(asynchronous.CreateCompleted() == 42 && asynchronous.ReloadCompleted() == 42
                && asynchronous.ReloadCompletedObserved() == 42 && asynchronous.SupersedeAndCancelPending() == 2);
        }
        finally
        {
            asynchronous.Cleanup();
        }

        var batch = new BatchBenchmarks();
        batch.Setup();
        try
        {
            Check(batch.Unbatched16() == 16 && batch.Batched16() == 32);
        }
        finally
        {
            batch.Cleanup();
        }

        var scheduler = new SchedulerBenchmarks();
        scheduler.Setup();
        try
        {
            Check(scheduler.Coalesced16() == 16 && scheduler.PauseResume() == 24);
        }
        finally
        {
            scheduler.Cleanup();
        }

        foreach (int depth in new[] { 1, 8, 32 })
        {
            var computed = new ComputedBenchmarks { Depth = depth };
            computed.Setup();
            try
            {
                Check(computed.ReadObservedCached() == depth && computed.ReadUnobservedCached() == depth);
                Check(computed.UpdateObserved() == depth + 1 && computed.UpdateAndReadUnobserved() == depth + 2);
            }
            finally
            {
                computed.Cleanup();
            }
        }

        foreach (int count in new[] { 0, 16, 256 })
        {
            var lifetime = new LifetimeBenchmarks { Observers = count };
            Check(lifetime.ScopeLifetime() == count * 2 && lifetime.FailureAndRetry() == 42
                && lifetime.ParallelWaveAsync().GetAwaiter().GetResult() == 128);
        }
    }

    private static void Check(bool condition)
    {
        if (!condition)
        {
            throw new InvalidOperationException("Signal workload contract failed.");
        }
    }
}
