using BenchmarkDotNet.Attributes;

namespace Lumyte.Graphics.Incremental.Benchmarks;

/// <summary>
/// Isolated 2D Update and Record boundaries. No Layout method is registered because 2D has no UI layout.
/// The 2d-static case intentionally measures input bookkeeping and immutable recording-cache lookup.
/// </summary>
/// <remarks>
/// One invocation and unroll factor one make IterationSetup per-operation in BenchmarkDotNet 0.15.8.
/// IterationTime is ignored; these are single-operation attribution probes, not throughput estimates.
/// </remarks>
[BenchmarkCategory("SingleOperationAttributionDiagnostic")]
[InvocationCount(1, 1)]
public class RenderingWorkloadContentPhaseBenchmarks
{
    private RenderingWorkloadPhaseState _state = null!;

    /// <summary>Gets or sets the 2D case identifier.</summary>
    [ParamsSource(nameof(Cases))]
    public string CaseName { get; set; } = null!;

    /// <summary>Gets or sets the logical object count.</summary>
    [ParamsSource(nameof(Counts))]
    public int Count { get; set; }

    /// <summary>Gets only actual 2D cases.</summary>
    public IEnumerable<string> Cases => RenderingWorkloadPhaseState.SelectedCases.Where(Content2DWorkloads.Cases.Contains);

    /// <summary>Gets the selected scale.</summary>
    public IEnumerable<int> Counts => RenderingWorkloadPhaseState.SelectedCounts;

    /// <summary>Creates and semantically checks the fixture outside measurements.</summary>
    [GlobalSetup]
    public void Setup() => _state = RenderingWorkloadPhaseState.Create(CaseName, Count);

    /// <summary>Starts an Update operation from a completed preceding frame.</summary>
    [IterationSetup(Target = nameof(Update))]
    public void PrepareUpdate() => _state.Prepare(nameof(Update));

    /// <summary>Advances deterministic inputs outside the timed recording boundary.</summary>
    [IterationSetup(Target = nameof(Record))]
    public void PrepareRecord() => _state.Prepare(nameof(Record));

    /// <summary>Measures deterministic geometry/transform input mutation or static bookkeeping.</summary>
    [Benchmark]
    public void Update() => _state.Workload.Update();

    /// <summary>Measures CPU recording or the explicitly static recording-cache policy.</summary>
    /// <returns>The ordered immutable CPU layers.</returns>
    [Benchmark]
    public object Record() => _state.Record();

    /// <summary>Checks Update and completes recording and fixture verification untimed.</summary>
    [IterationCleanup(Target = nameof(Update))]
    public void CheckUpdate() => _state.Check(nameof(Update));

    /// <summary>Checks recording semantics and fixture invariants untimed.</summary>
    [IterationCleanup(Target = nameof(Record))]
    public void CheckRecord() => _state.Check(nameof(Record));

    /// <summary>Verifies the fixture and always releases it on verification failure.</summary>
    [GlobalCleanup]
    public void Cleanup() => _state?.Dispose();
}
