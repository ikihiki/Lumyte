namespace Lumyte.Core.Time;

/// <summary>A deterministic monotonic clock for tests and simulations.</summary>
public sealed class ManualClock : IMonotonicClock
{
    public TimePoint Now { get; private set; }

    public ManualClock(TimePoint initial = default) => Now = initial;

    public void Advance(Duration duration)
    {
        if (duration < Duration.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(duration), "A monotonic clock cannot move backwards.");
        }

        Now += duration;
    }
}
