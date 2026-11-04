namespace Lumyte.Graphics.BenchmarkWorkloads;

/// <summary>A dedicated post-full-GC observation, outside every frame timing interval.</summary>
/// <param name="Stage">Baseline, initialized, cycle-return or after-dispose.</param>
/// <param name="Cycle">One-based roundtrip, or null outside cycle observations.</param>
/// <param name="LogicalFrame">Source frame, or null when no fixture is owned.</param>
/// <param name="RetainedGenerations">Actual history count, zero without fixture ownership.</param>
/// <param name="FixtureOwned">Whether the probe deliberately owns the fixture during collection.</param>
/// <param name="ManagedLiveHeapBytes">Whole-process managed live heap after blocking full GC.</param>
/// <param name="ProcessRssBytes">Process working set refreshed after the same collection.</param>
public sealed record RetentionMemorySample(string Stage, int? Cycle, int? LogicalFrame, int RetainedGenerations, bool FixtureOwned, long ManagedLiveHeapBytes, long ProcessRssBytes)
{
    /// <summary>Gets unavailable native allocation accounting; RSS is not a substitute.</summary>
    public long? NativeAllocationBytes => null;

    /// <summary>Gets unavailable GPU allocation accounting; this managed-only probe creates no device.</summary>
    public long? GpuAllocationBytes => null;
}
