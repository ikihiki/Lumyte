using BenchmarkDotNet.Attributes;
using Lumyte.Audio.Abstractions;

namespace Lumyte.Audio.Execution.Benchmark;

/// <summary>Real OpenAL commands dispatched to a host or dedicated execution owner.</summary>
[BenchmarkCategory("Audio", "Execution", "Native", "Screening", "Comparison", "Diagnostic")]
public class NativeExecutionBenchmarks : ExecutionWorkload
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
    protected override IAudioDevice CreateBackend()
    {
        if (Environment.GetEnvironmentVariable("ALSOFT_DRIVERS") != "null")
        {
            throw new InvalidOperationException("Set ALSOFT_DRIVERS=null before process startup; physical output is prohibited.");
        }

        return new OpenAL.OpenALDevice();
    }
}
