using BenchmarkDotNet.Attributes;

namespace Lumyte.UI.Application.Desktop.Benchmark.HostCompletedFrame;

/// <summary>Eight standard-wgpu serial, system-paced full-host completion cases; not CPU-only or physical scanout measurements.</summary>
[MemoryDiagnoser]
[BenchmarkCategory("Screening", "Comparison", "Diagnostic")]
public class DesktopFrameBenchmarks
{
    private DesktopFrameFixture _fixture = null!;

    /// <summary>Gets or sets the exact visible independently reactive rectangle count, excluding container nodes.</summary>
    [Params(100, 1000)]
    public int ItemCount { get; set; }

    /// <summary>Starts and validates the real standard host outside timing.</summary>
    /// <returns>Untimed startup and analytic pixel verification.</returns>
    [GlobalSetup]
    public async Task SetupAsync()
    {
        _fixture = new(ItemCount);
        await _fixture.StartAsync();
        await _fixture.CompleteAsync(1);
        await _fixture.VerifyPixelsAsync();
        await _fixture.CompleteAsync(ItemCount);
        await _fixture.VerifyPixelsAsync();
        await _fixture.CompleteAsync(0, localLayout: true);
        await _fixture.VerifyPixelsAsync();
        await _fixture.CompleteAsync(ItemCount, localLayout: true);
        await _fixture.CompleteAsync(1);
        await _fixture.VerifyPixelsAsync();
    }

    /// <summary>Accepts and completes a new unchanged ordinary retained host frame.</summary>
    /// <returns>Actual matching completed frame identity.</returns>
    [Benchmark]
    public Task<long> StaticAsync() => _fixture.CompleteAsync(0);

    /// <summary>Changes one actual UI paint signal and completes its newly accepted host frame.</summary>
    /// <returns>Actual matching completed frame identity.</returns>
    [Benchmark]
    public Task<long> LocalPaintAsync() => _fixture.CompleteAsync(1);

    /// <summary>Changes every independently bound paint signal and completes its newly accepted host frame.</summary>
    /// <returns>Actual matching completed frame identity.</returns>
    [Benchmark]
    public Task<long> FullDirtyAsync() => _fixture.CompleteAsync(ItemCount);

    /// <summary>Changes one actual width signal and completes its propagated layout and retained host frame.</summary>
    /// <returns>Actual matching completed frame identity.</returns>
    [Benchmark]
    public Task<long> LocalLayoutAsync() => _fixture.CompleteAsync(0, localLayout: true);

    /// <summary>Validates final output and drains the actual host outside timing.</summary>
    /// <returns>Untimed final verification and ordered release.</returns>
    [GlobalCleanup]
    public async Task CleanupAsync()
    {
        try
        {
            await _fixture.VerifyPixelsAsync();
        }
        finally
        {
            await _fixture.DisposeAsync();
        }
    }
}
