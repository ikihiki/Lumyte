namespace Lumyte.Resources;

/// <summary>Controls bounded change aggregation and retries for automatic resource reloads.</summary>
public sealed record ResourceReloadOptions
{
    /// <summary>Gets a value indicating whether the manager subscribes to its source's change notifications.</summary>
    public bool Enabled { get; init; } = true;

    /// <summary>Gets the quiet period after the latest hint before a batch starts.</summary>
    public TimeSpan DebounceDelay { get; init; } = TimeSpan.FromMilliseconds(100);

    /// <summary>Gets the longest aggregation period, including continuously arriving hints.</summary>
    public TimeSpan MaximumBatchDelay { get; init; } = TimeSpan.FromSeconds(1);

    /// <summary>Gets the maximum number of distinct pending identifiers before requesting a full rescan.</summary>
    public int MaximumPendingChanges { get; init; } = 1024;

    /// <summary>Gets the number of retries after an initial conflict or failed reload.</summary>
    public int MaximumRetryCount { get; init; } = 3;

    /// <summary>Gets the delay between retries; notifications continue accumulating during this delay.</summary>
    public TimeSpan RetryDelay { get; init; } = TimeSpan.FromMilliseconds(100);

    internal void Validate()
    {
        ValidateDelay(DebounceDelay, nameof(DebounceDelay));
        ValidateDelay(MaximumBatchDelay, nameof(MaximumBatchDelay));
        ValidateDelay(RetryDelay, nameof(RetryDelay));
        ArgumentOutOfRangeException.ThrowIfLessThan(MaximumBatchDelay, DebounceDelay);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(MaximumBatchDelay, TimeSpan.Zero);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(MaximumPendingChanges);
        ArgumentOutOfRangeException.ThrowIfNegative(MaximumRetryCount);
    }

    private static void ValidateDelay(TimeSpan value, string name)
    {
        if (value < TimeSpan.Zero || value > TimeSpan.FromDays(1))
        {
            throw new ArgumentOutOfRangeException(name, "The delay must be between zero and one day.");
        }
    }
}
