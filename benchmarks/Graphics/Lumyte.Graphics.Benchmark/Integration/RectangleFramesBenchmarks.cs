using BenchmarkDotNet.Attributes;

namespace Lumyte.Graphics.Benchmark.Integration;

/// <summary>CPU wall latency per completed headless logical frame, including dynamic recording, preparation, submission, waiting and retirement.</summary>
[MemoryDiagnoser]
[BenchmarkCategory("Screening", "Comparison", "Diagnostic")]
public class RectangleFramesBenchmarks
{
    private RectangleFrameWorkload _workload = null!;

    /// <summary>Gets or sets the actual rectangle count.</summary>
    [Params(1024, 10000)]
    public int RectangleCount { get; set; }

    /// <summary>Gets or sets whether immutable input is reused or rebuilt during each frame.</summary>
    [Params(RectangleInputProfile.StaticReuse, RectangleInputProfile.OneChanged, RectangleInputProfile.AllChanged)]
    public RectangleInputProfile Input { get; set; }

    /// <summary>Gets or sets the bounded number of independent in-flight frame slots.</summary>
    [Params(1, 3)]
    public int FramesInFlight { get; set; }

    /// <summary>Creates the GPU only after BDN selects and sets up a case; primes the warm renderer outside timing.</summary>
    /// <returns>Untimed setup completion.</returns>
    [GlobalSetup]
    public async Task SetupAsync()
    {
        _workload = new(RectangleCount, Input, FramesInFlight);
        Console.WriteLine(_workload.Identity);
        await _workload.RunAsync().ConfigureAwait(false);
        await _workload.VerifyLastFrameAsync().ConfigureAwait(false);
    }

    /// <summary>Executes exactly six logical frames with final drain inside measurement.</summary>
    /// <returns>Completion of all frame submissions and completed-resource cleanup.</returns>
    [Benchmark(OperationsPerInvoke = RectangleFrameWorkload.FramesPerOperation)]
    public Task CompletedFramesAsync() => _workload.RunAsync();

    /// <summary>Rechecks the completed final frame and returns resources outside timing.</summary>
    /// <returns>Untimed teardown completion.</returns>
    [GlobalCleanup]
    public async Task CleanupAsync()
    {
        try
        {
            await _workload.VerifyLastFrameAsync().ConfigureAwait(false);
        }
        finally
        {
            await _workload.DisposeAsync().ConfigureAwait(false);
        }
    }
}
