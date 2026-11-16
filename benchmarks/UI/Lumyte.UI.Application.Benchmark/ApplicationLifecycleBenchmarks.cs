using BenchmarkDotNet.Attributes;

namespace Lumyte.UI.Application.Benchmark;

/// <summary>Separates passive Build/DI from a fresh one-way full application lifecycle.</summary>
[MemoryDiagnoser]
public class ApplicationLifecycleBenchmarks
{
    /// <summary>Builds validated DI and disposes an unstarted application, without a root factory call.</summary>
    /// <returns>Complete passive teardown.</returns>
    [Benchmark]
    [BenchmarkCategory("Screening", "Comparison")]
    public async Task UiApplicationColdBuildDiDisposeAsync()
    {
        await using var fixture = new ApplicationFixture(100, false);
    }

    /// <summary>Builds a fresh application, initializes a real owner, renders a frame, stops and disposes.</summary>
    /// <returns>Complete lifecycle, never a reused stopped owner.</returns>
    [Benchmark]
    [BenchmarkCategory("Screening", "Comparison")]
    public async Task UiApplicationColdBuildFirstFrameStopDisposeAsync()
    {
        await using var fixture = new ApplicationFixture(100, false);
        await fixture.StartAsync();
    }
}
