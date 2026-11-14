using BenchmarkDotNet.Attributes;
using Lumyte.Audio.Abstractions;

namespace Lumyte.Audio.Execution.Benchmark;

/// <summary>CPU-only generated audio command dispatch against an owner-checking public-contract backend.</summary>
[BenchmarkCategory("Audio", "Execution", "CPU", "Screening", "Comparison", "Diagnostic")]
public class CpuExecutionBenchmarks : ExecutionWorkload
{
    /// <summary>Runs the retained individual control workload.</summary>
    /// <returns>The observed voice count.</returns>
    [Benchmark]
    public new int IndividualControls() => base.IndividualControls();

    /// <summary>Runs the retained atomic batch control workload.</summary>
    /// <returns>The observed voice count.</returns>
    [Benchmark]
    public new int BatchedControls() => base.BatchedControls();

    /// <summary>Runs the retained ordered resource lifecycle workload.</summary>
    /// <returns>The observed frame count.</returns>
    [Benchmark]
    public new long ResourceLifecycle() => base.ResourceLifecycle();

    /// <inheritdoc/>
    protected override IAudioDevice CreateBackend() => new CountingAudioDevice();
}
