namespace Lumyte.Execution;

/// <summary>Periodic owner update and control-drain budgets.</summary>
public sealed class ThreadLoopOptions
{
    /// <summary>Initializes a new instance of the <see cref="ThreadLoopOptions"/> class.</summary>
    /// <param name="period">The positive period.</param>
    /// <param name="maxControlWorkItems">The positive item budget.</param>
    /// <param name="maxControlTime">The positive time budget, less than the period.</param>
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

    /// <summary>Gets the maximum control count per period.</summary>
    public int MaxControlWorkItems { get; }

    /// <summary>Gets the maximum control execution time per period.</summary>
    public TimeSpan MaxControlTime { get; }
}
