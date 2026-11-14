using BenchmarkDotNet.Attributes;

namespace Lumyte.Execution.Benchmark;

/// <summary>Separates plan preparation, runtime start, and runtime stop boundaries.</summary>
[MemoryDiagnoser]
[BenchmarkCategory("Core", "Execution", "Screening", "Comparison", "Diagnostic")]
public class RuntimeLifecycleBenchmarks
{
    private Fixture? _fixture;

    /// <summary>Forces preparation to one invocation per iteration without leaking prior plans.</summary>
    [IterationSetup(Target = nameof(Prepare))]
    public void PreparePlan() => _fixture = null;

    /// <summary>Creates the already-prepared fixture for a start-only operation.</summary>
    [IterationSetup(Target = nameof(Start))]
    public void PrepareStart() => _fixture = new Fixture(start: false);

    /// <summary>Creates a running fixture for a stop-only operation.</summary>
    [IterationSetup(Target = nameof(Stop))]
    public void PrepareStop() => _fixture = new Fixture();

    /// <summary>Releases the current fixture outside the operation boundary.</summary>
    [IterationCleanup]
    public void Cleanup()
    {
        _fixture?.Dispose();
        _fixture = null;
    }

    /// <summary>Collects modules and prepares a plan without starting or stopping it.</summary>
    /// <returns>The prepared status.</returns>
    [Benchmark]
    [BenchmarkCategory("Configuration")]
    public ExecutionStatus Prepare()
    {
        _fixture = new Fixture(start: false);
        return _fixture.Runtime.Status;
    }

    /// <summary>Starts only a freshly prepared runtime.</summary>
    /// <returns>The running status.</returns>
    [Benchmark]
    [BenchmarkCategory("Lifecycle")]
    public ExecutionStatus Start()
    {
        _fixture!.Wait(_fixture.Runtime.StartAsync().AsTask());
        return _fixture.Runtime.Status;
    }

    /// <summary>Stops only an already-running runtime.</summary>
    /// <returns>The stopped status.</returns>
    [Benchmark]
    [BenchmarkCategory("Lifecycle")]
    public ExecutionStatus Stop()
    {
        _fixture!.Wait(_fixture.Runtime.StopAsync().AsTask());
        return _fixture.Runtime.Status;
    }
}
