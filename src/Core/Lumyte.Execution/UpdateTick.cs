namespace Lumyte.Execution;

/// <summary>Timing of one owner update.</summary>
public readonly struct UpdateTick
{
    internal UpdateTick(TimeSpan elapsed, long index, long skipped, TimeSpan scheduled, TimeSpan started)
    {
        Elapsed = elapsed;
        Index = index;
        SkippedTicks = skipped;
        ScheduledTime = scheduled;
        StartedTime = started;
    }

    /// <summary>Gets actual time since the previous update.</summary>
    public TimeSpan Elapsed { get; }

    /// <summary>Gets the absolute update index.</summary>
    public long Index { get; }

    /// <summary>Gets skipped scheduled ticks.</summary>
    public long SkippedTicks { get; }

    /// <summary>Gets monotonic scheduled time.</summary>
    public TimeSpan ScheduledTime { get; }

    /// <summary>Gets monotonic start time.</summary>
    public TimeSpan StartedTime { get; }
}
