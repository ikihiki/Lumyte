namespace Lumyte.Core.Time;

/// <summary>A deterministic monotonic clock for tests and simulations.</summary>
public sealed class ManualClock : IMonotonicClock
{
    /// <summary>Initializes a new instance of the <see cref="ManualClock"/> class.</summary>
    /// <param name="initial">The initial.</param>
    public ManualClock(TimePoint initial = default)
    {
        Now = initial;
    }

    /// <summary>Gets the current monotonic time.</summary>
    public TimePoint Now { get; private set; }

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
