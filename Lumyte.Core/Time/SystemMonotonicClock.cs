using System.Diagnostics;

namespace Lumyte.Core.Time;

/// <summary>A process-local monotonic clock backed by <see cref="Stopwatch"/>.</summary>
public sealed class SystemMonotonicClock : IMonotonicClock
{
    private readonly long _origin = Stopwatch.GetTimestamp();

    public TimePoint Now
    {
        get
        {
            TimeSpan elapsed = Stopwatch.GetElapsedTime(_origin, Stopwatch.GetTimestamp());
            return TimePoint.FromTicks(elapsed.Ticks);
        }
    }
}
