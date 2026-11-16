using BenchmarkDotNet.Attributes;

namespace Lumyte.UI.Application.Benchmark;

/// <summary>Current retained-snapshot headless owner; no GPU, OS or transport.</summary>
[MemoryDiagnoser]
public class ApplicationFrameBenchmarks
{
    private ApplicationFixture _fixture = null!;

    /// <summary>Gets or sets the actual ordinary-control count.</summary>
    [Params(100, 1000)]
    public int Elements { get; set; }

    /// <summary>Gets or sets a value indicating whether local diagnostics participate in the control comparison.</summary>
    [Params(false, true)]
    public bool Diagnostics { get; set; }

    /// <summary>Starts one owner and completes its first public frame barrier.</summary>
    /// <returns>Startup completion.</returns>
    [GlobalSetup]
    public async Task SetupAsync()
    {
        _fixture = new(Elements, Diagnostics);
        await _fixture.StartAsync();
    }

    /// <summary>Stops, joins and disposes the actual owner.</summary>
    /// <returns>Complete teardown.</returns>
    [GlobalCleanup]
    public Task Cleanup() => _fixture.DisposeAsync().AsTask();

    /// <summary>Alternates real model values and waits for the retained host frame.</summary>
    /// <returns>Completed frame ordinal.</returns>
    [Benchmark]
    [BenchmarkCategory("Screening", "Comparison")]
    public Task<int> UiApplicationSteadyRetainedFrame() => _fixture.AdvanceAsync();
}
