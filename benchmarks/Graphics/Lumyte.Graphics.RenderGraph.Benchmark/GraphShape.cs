namespace Lumyte.Graphics.RenderGraph.Benchmark.Compilation;

/// <summary>Bounded graph shapes; independent branches are dependencies, not multithreaded execution.</summary>
public enum GraphShape
{
    /// <summary>A serial copy chain.</summary>
    Chain,

    /// <summary>Independent branches joined through explicit dependencies.</summary>
    FanOutJoin,

    /// <summary>Repeated imports reuse one handle; copies alternate disjoint ranges of the borrowed allocation.</summary>
    ImportedAlias,
}
