namespace Lumyte.Core.Time;

/// <summary>A deterministic monotonic clock for tests and simulations.</summary>
/// <param name="initial">The initial monotonic time.</param>
public sealed class ManualClock(TimePoint initial = default) : IMonotonicClock
{
    /// <summary>Gets the current monotonic time.</summary>
    public TimePoint Now { get; private set; } = initial;

    /// <summary>Advances the monotonic clock by a nonnegative duration.</summary>
    /// <param name="duration">The duration.</param>
    public void Advance(Duration duration)
    {
        if (duration < Duration.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(duration), "A monotonic clock cannot move backwards.");
        }

        Now += duration;
    }
}
