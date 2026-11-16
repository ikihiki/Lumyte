using BenchmarkDotNet.Attributes;
using Widget = Lumyte.UI.Compose.Definitions.Widget;

namespace Lumyte.UI.Benchmark;

/// <summary>Passive construction versus mounted runtime operations.</summary>
[MemoryDiagnoser]
[BenchmarkCategory("Screening", "Comparison", "Diagnostic")]
public class TreeBenchmarks
{
    private UiTreeWorkload _workload = null!;
    private Widget _declaration = null!;

    /// <summary>Gets or sets actual leaf count.</summary>
    [Params(16, 128)]
    public int Count { get; set; }

    /// <summary>Creates fixture state.</summary>
    [GlobalSetup]
    public void Setup()
    {
        _workload = new(Count);
        _declaration = _workload.Declare();
    }

    /// <summary>Constructs passive declarations.</summary>
    /// <returns>The tree.</returns>
    [Benchmark]
    public Widget Declare() => _workload.Declare();

    /// <summary>Mounts and disposes independent runtime elements.</summary>
    /// <returns>The root identity whose mounted lifetime ended.</returns>
    [Benchmark]
    public UiElement MountDispose()
    {
        using var surface = new UiSurface(_declaration);
        return surface.RootElement;
    }

    /// <summary>Reorders stable keys.</summary>
    [Benchmark]
    public void ReorderStableKeys() => _workload.Reconcile(false);

    /// <summary>Alternates disjoint identity sets.</summary>
    [Benchmark]
    public void ReplaceStructure() => _workload.Reconcile(true);

    /// <summary>Mutates one signal and updates its generated function.</summary>
    [Benchmark]
    public void UpdateFunction() => _workload.UpdateFunction();

    /// <summary>Releases runtime state.</summary>
    [GlobalCleanup]
    public void Cleanup() => _workload.Dispose();
}
