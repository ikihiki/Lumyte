namespace Lumyte.Resources.Benchmark;

/// <summary>Deterministic resource benchmark oracles.</summary>
public static class BenchmarkChecks
{
    /// <summary>Checks values, graph release counts, embedded checksums and independent stream cursors.</summary>
    public static void Verify()
    {
        foreach (int depth in new[] { 1, 8, 32 })
        {
            var benchmark = new ResourcesBenchmarks { Depth = depth };
            benchmark.Setup();
            try
            {
                Check(benchmark.ConstructKey().Value == "asset/material/diffuse");
                Check(benchmark.CacheHit() == depth && benchmark.RetainHandle() == depth && benchmark.Snapshot() == depth);
                Check(benchmark.BuildChainAsync().GetAwaiter().GetResult() == depth);
                Check(benchmark.BuildAndTrimAsync().GetAwaiter().GetResult() == depth);
            }
            finally
            {
                benchmark.Cleanup();
            }
        }

        Check(new ReloadBenchmarks().FailedReloadPreservesPublishedAsync().GetAwaiter().GetResult() == 1);
        Check(new ReloadBenchmarks().ChangeAndPublishAsync().GetAwaiter().GetResult() == 2);
        Check(new RuntimeGenerationBenchmarks().InvalidateAndRebuildAsync().GetAwaiter().GetResult() == 2);
        foreach (int callers in new[] { 1, 8, 64 })
        {
            Check(new ConcurrentAcquireBenchmarks { Callers = callers }.SharedBuildAsync().GetAwaiter().GetResult() == 1);
        }

        var integration = new IntegrationBenchmarks();
        integration.Setup();
        try
        {
            Check(integration.BeforeStartRejectedAsync().GetAwaiter().GetResult());
            Check(integration.RegisterAndResolveAsync().GetAwaiter().GetResult());
            Check(integration.EmbeddedRangeAsync().GetAwaiter().GetResult() == 206);
            Check(integration.EmbeddedPipelineAsync().GetAwaiter().GetResult() == 525);
            Check(integration.ExecutionChainAsync().GetAwaiter().GetResult() == 4);
        }
        finally
        {
            integration.Cleanup();
        }

        foreach (int length in new[] { 0, 4096, 65536 })
        {
            var stream = new StreamingBenchmarks { Length = length };
            stream.Setup();
            try
            {
                Check(stream.ReadAllAsync().GetAwaiter().GetResult() == (long)length * (length - 1) / 2);
                Check(stream.SeekAndReadAsync().GetAwaiter().GetResult() == (length == 0 ? -1 : length / 2));
            }
            finally
            {
                stream.Cleanup();
            }
        }
    }

    private static void Check(bool condition)
    {
        if (!condition)
        {
            throw new InvalidOperationException("Resource benchmark contract failed.");
        }
    }
}
