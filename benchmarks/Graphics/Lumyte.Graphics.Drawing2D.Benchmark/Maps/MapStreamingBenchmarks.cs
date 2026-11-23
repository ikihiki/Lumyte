using BenchmarkDotNet.Attributes;

namespace Lumyte.Graphics.Drawing2D.Benchmark.Maps;

/// <summary>Cold local spatial pipeline including controlled arrival/publication/LRU/manager trim and final cleanup; no HTTP or GPU.</summary>
[MemoryDiagnoser]
[BenchmarkCategory("Screening", "Comparison", "Diagnostic")]
public class MapStreamingBenchmarks
{
    /// <summary>Executes the whole bounded A-B-A cycle, including fixture registration and resource ownership.</summary>
    /// <returns>Actual executed stage counts.</returns>
    [Benchmark]
    public Task<StreamingObservation> ControlledArrivalPublicationRetirementTrimAsync() => MapStreamingWorkload.RunAsync();
}
