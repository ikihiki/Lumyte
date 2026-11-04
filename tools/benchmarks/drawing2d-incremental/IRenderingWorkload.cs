using Lumyte.Graphics.Drawing2D;

namespace Lumyte.Graphics.Incremental.Benchmarks;

/// <summary>Separates real workload update, layout, CPU recording and GPU execution.</summary>
internal interface IRenderingWorkload : IDisposable
{
    /// <summary>Gets the workload family.</summary>
    string Suite { get; }

    /// <summary>Gets the unique case name.</summary>
    string Name { get; }

    /// <summary>Gets the exercised production path and limits.</summary>
    string Description { get; }

    /// <summary>Gets the deterministic logical frame number.</summary>
    int Frame { get; }

    /// <summary>Gets workload input and observed operation counters.</summary>
    IReadOnlyDictionary<string, long> Counters { get; }

    /// <summary>Advances deterministic input state.</summary>
    void Update();

    /// <summary>Applies actual UI layout and text shaping, where applicable.</summary>
    void Layout();

    /// <summary>Records or reuses immutable ordered layers.</summary>
    /// <returns>The CPU drawing snapshots; no GPU execution is included.</returns>
    IReadOnlyList<DrawList2D> Record();

    /// <summary>Checks untimed workload invariants.</summary>
    void Verify();
}
