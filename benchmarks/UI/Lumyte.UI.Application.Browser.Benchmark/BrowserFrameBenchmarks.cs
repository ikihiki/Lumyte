using BenchmarkDotNet.Attributes;

namespace Lumyte.UI.Application.Browser.Benchmark;

/// <summary>Standard BDN end-to-end wall latency, including IPC/dispatch/pacing; not pure browser CPU or GPU timestamps.</summary>
[MemoryDiagnoser]
public class BrowserFrameBenchmarks
{
    private BrowserSession _session = null!;

    /// <summary>Gets or sets actual visible retained UI size and mutation coverage.</summary>
    // Full1000 remains catalog-visible and fails closed while the recorded production issue is unresolved.
    [Params("Local100", "Full1000")]
    public string Profile { get; set; } = "Local100";

    /// <summary>Starts an audited hardware browser using prepublished WASM, outside measurement.</summary>
    /// <returns>Initial frame completion.</returns>
    [GlobalSetup]
    public async Task SetupAsync()
    {
        _session = new();
        await _session.StartAsync(Profile);
    }

    /// <summary>Measures host-to-new-presented-frame response, including IPC, owner dispatch and pacing.</summary>
    /// <returns>The matching actual frame identity.</returns>
    [Benchmark(Description = "Browser frame wall latency (IPC/dispatch/pacing included)")]
    [BenchmarkCategory("Screening", "Comparison", "Diagnostic")]
    public Task<long> HostBrowserUpdatePresentedFrameAsync() => _session.AdvanceAsync();

    /// <summary>Closes only owned browser resources.</summary>
    /// <returns>Bounded teardown.</returns>
    [GlobalCleanup]
    public Task CleanupAsync() => _session.DisposeAsync().AsTask();
}
