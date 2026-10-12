namespace Lumyte.Diagnostics;

/// <summary>Trusted operation scheduling and retention classes.</summary>
public enum DiagnosticOperationTraffic
{
    /// <summary>Ordinary diagnostic queries and edits.</summary>
    Standard,

    /// <summary>Frequent transient updates with a deadline of at most one second.</summary>
    Stream,

    /// <summary>Lease heartbeats and stop commands with reserved queue capacity.</summary>
    Control,
}
