namespace Lumyte.Execution;

/// <summary>Periodic owner updates and budgets for one control-drain turn.</summary>
public sealed class ThreadLoopOptions
{
    /// <summary>Initializes a new instance of the <see cref="ThreadLoopOptions"/> class.</summary>
    /// <param name="period">The positive period.</param>
    /// <param name="maxControlWorkItems">The positive item budget per control turn.</param>
    /// <param name="maxControlTime">The positive time budget per control turn, less than the period.</param>
    public ThreadLoopOptions(TimeSpan period, int maxControlWorkItems, TimeSpan maxControlTime)
    {
        if (period <= TimeSpan.Zero || maxControlTime <= TimeSpan.Zero || maxControlTime >= period)
        {
            throw new ArgumentOutOfRangeException(nameof(period));
        }

        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxControlWorkItems);
        Period = period;
        MaxControlWorkItems = maxControlWorkItems;
        MaxControlTime = maxControlTime;
    }

    /// <summary>Gets the update period.</summary>
    public TimeSpan Period { get; }

    /// <summary>Gets the maximum control count before checking due updates again.</summary>
    public int MaxControlWorkItems { get; }

    /// <summary>Gets the maximum control execution time before checking due updates again.</summary>
    /// <remarks>The next eligible update deadline can shorten this budget. An executing handler is never interrupted.</remarks>
    public TimeSpan MaxControlTime { get; }
}
