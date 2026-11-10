using BenchmarkDotNet.Attributes;

namespace Lumyte.Graphics.Incremental.Benchmarks;

/// <summary>
/// Isolated real UI stages. Layout includes any nested text shaping; it is not an additive text-only cost.
/// Static HUD/editor cases intentionally measure the existing cached/clean-stage policy.
/// </summary>
/// <remarks>
/// BenchmarkDotNet 0.15.8 has no InvocationSetup attribute. One invocation and unroll factor one
/// make each targeted IterationSetup a per-operation boundary. IterationTime is consequently ignored;
/// these short single-operation probes are attribution diagnostics, not throughput/frame-latency estimates.
/// Update may itself reconcile/shape text in the fixture; stages describe API boundaries, not exclusive subsystems.
/// </remarks>
[BenchmarkCategory("SingleOperationAttributionDiagnostic")]
[InvocationCount(1, 1)]
public class RenderingWorkloadPhaseBenchmarks
{
    private RenderingWorkloadPhaseState _state = null!;

    /// <summary>Gets or sets the UI case identifier.</summary>
    [ParamsSource(nameof(Cases))]
    public string CaseName { get; set; } = null!;

    /// <summary>Gets or sets the logical workload scale.</summary>
    [ParamsSource(nameof(Counts))]
    public int Count { get; set; }

    /// <summary>Gets only actual UI cases; there is no empty 2D layout benchmark.</summary>
    public IEnumerable<string> Cases => RenderingWorkloadPhaseState.SelectedCases.Where(name => !Content2DWorkloads.Cases.Contains(name));

    /// <summary>Gets the selected scale.</summary>
    public IEnumerable<int> Counts => RenderingWorkloadPhaseState.SelectedCounts;

    /// <summary>Creates and semantically checks the fixture outside measurements.</summary>
    [GlobalSetup]
    public void Setup() => _state = RenderingWorkloadPhaseState.Create(CaseName, Count);

    /// <summary>Starts an Update operation from a completed preceding frame.</summary>
    [IterationSetup(Target = nameof(Update))]
    public void PrepareUpdate() => _state.Prepare(nameof(Update));

    /// <summary>Advances deterministic inputs outside the timed inclusive Layout boundary.</summary>
    [IterationSetup(Target = nameof(Layout))]
    public void PrepareLayout() => _state.Prepare(nameof(Layout));

    /// <summary>Advances inputs and completes layout outside the timed Record boundary.</summary>
    [IterationSetup(Target = nameof(Record))]
    public void PrepareRecord() => _state.Prepare(nameof(Record));

    /// <summary>Measures deterministic input mutation and any reconciliation it performs.</summary>
    [Benchmark]
    public void Update() => _state.Workload.Update();

    /// <summary>Measures real UiSurface layout, inclusive of nested text layout if dirty.</summary>
    [Benchmark]
    public void Layout() => _state.Workload.Layout();

    /// <summary>Measures recording or explicitly cached UI reuse, retaining three ordered snapshots.</summary>
    /// <returns>The ordered immutable CPU layers.</returns>
    [Benchmark]
    public object Record() => _state.Record();

    /// <summary>Checks Update, completes its remaining stages and verifies fixture semantics untimed.</summary>
    [IterationCleanup(Target = nameof(Update))]
    public void CheckUpdate() => _state.Check(nameof(Update));

    /// <summary>Checks Layout and completes recording and verification untimed.</summary>
    [IterationCleanup(Target = nameof(Layout))]
    public void CheckLayout() => _state.Check(nameof(Layout));

    /// <summary>Checks recording counters, nonempty layers and fixture semantics untimed.</summary>
    [IterationCleanup(Target = nameof(Record))]
    public void CheckRecord() => _state.Check(nameof(Record));

    /// <summary>Verifies the completed fixture and always releases it, even on verification failure.</summary>
    [GlobalCleanup]
    public void Cleanup() => _state?.Dispose();
}
