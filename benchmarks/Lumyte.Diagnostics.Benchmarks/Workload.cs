namespace Lumyte.Diagnostics.Benchmarks;

/// <summary>Different diagnostic workload shapes.</summary>
public enum Workload
{
    /// <summary>A small control result.</summary>
    Operation,

    /// <summary>A metrics batch.</summary>
    Metrics,

    /// <summary>A completed trace batch.</summary>
    Trace,

    /// <summary>A structured log batch.</summary>
    Log,

    /// <summary>A graph snapshot.</summary>
    Graph,

    /// <summary>An opaque image payload, included to test binary transfer costs.</summary>
    Image,
}
