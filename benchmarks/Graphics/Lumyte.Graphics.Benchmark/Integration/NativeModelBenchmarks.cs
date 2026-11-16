using BenchmarkDotNet.Attributes;

namespace Lumyte.Graphics.Benchmark.Integration;

/// <summary>Real-backend CPU preparation and recording, excluding submit/completion and GPU execution.</summary>
[MemoryDiagnoser]
[BenchmarkCategory("Screening", "Comparison", "Diagnostic")]
public class NativeModelBenchmarks
{
    private NativeModelWorkload _workload = null!;

    /// <summary>Gets or sets independently transformed draw count.</summary>
    [Params(1, 32)]
    public int Count { get; set; }

    /// <summary>Gets or sets the descriptor and pass mutation scenario.</summary>
    [Params("Numeric", "Static", "Mutation", "CrossPass")]
    public string Scenario { get; set; } = "Numeric";

    /// <summary>Creates retained resources and warms native pipeline variants outside timing.</summary>
    [GlobalSetup]
    public void Setup()
    {
        _workload = new();
        _workload.Verify();
        if (_workload.RecordInstances(Count, scenario: Scenario) != (ulong)Count || _workload.States(Count) != Count)
        {
            throw new InvalidOperationException("Native model workload did not execute.");
        }
    }

    /// <summary>Builds changing numeric arguments, registers roots, records indexed draws and discards finished commands.</summary>
    /// <returns>Recorded draw count.</returns>
    [Benchmark]
    public ulong RecordIndexed() => _workload.RecordInstances(Count, scenario: Scenario);

    /// <summary>Isolates SetRenderState validation/snapshot plus command/pass lifetime.</summary>
    /// <returns>State count.</returns>
    [Benchmark]
    public int PrepareStates() => _workload.States(Count);

    /// <summary>Releases retained resources after discarded commands.</summary>
    [GlobalCleanup]
    public void Cleanup() => _workload.Dispose();
}
