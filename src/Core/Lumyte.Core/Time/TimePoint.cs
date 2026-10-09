namespace Lumyte.Core.Time;

/// <summary>A point on a monotonic clock. It has no wall-clock meaning.</summary>
public readonly record struct TimePoint : IComparable<TimePoint>
{
    private TimePoint(long ticks)
    {
        Ticks = ticks;
    }

    /// <summary>Gets the zero.</summary>
    public static TimePoint Zero => default;

    /// <summary>Gets the ticks.</summary>
    public long Ticks { get; }

    /// <summary>Calculates a checked time operation.</summary>
    /// <param name="left">The left.</param>
    /// <param name="right">The right.</param>
    /// <returns>The resulting time value.</returns>
    public static Duration operator -(TimePoint left, TimePoint right) => Duration.FromTicks(checked(left.Ticks - right.Ticks));

    /// <summary>Calculates a checked time operation.</summary>
    /// <param name="point">The point.</param>
    /// <param name="duration">The duration.</param>
    /// <returns>The resulting time value.</returns>
    public static TimePoint operator +(TimePoint point, Duration duration) => new(checked(point.Ticks + duration.Ticks));

    /// <summary>Calculates a checked time operation.</summary>
    /// <param name="point">The point.</param>
    /// <param name="duration">The duration.</param>
    /// <returns>The resulting time value.</returns>
    public static TimePoint operator -(TimePoint point, Duration duration) => new(checked(point.Ticks - duration.Ticks));

    /// <summary>Calculates a checked time operation.</summary>
    /// <param name="left">The left.</param>
    /// <param name="right">The right.</param>
    /// <returns>The resulting time value.</returns>
    public static bool operator <(TimePoint left, TimePoint right) => left.Ticks < right.Ticks;

    /// <summary>Calculates a checked time operation.</summary>
    /// <param name="left">The left.</param>
    /// <param name="right">The right.</param>
    /// <returns>The resulting time value.</returns>
    public static bool operator >(TimePoint left, TimePoint right) => left.Ticks > right.Ticks;

    /// <summary>Calculates a checked time operation.</summary>
    /// <param name="left">The left.</param>
    /// <param name="right">The right.</param>
    /// <returns>The resulting time value.</returns>
    public static bool operator <=(TimePoint left, TimePoint right) => left.Ticks <= right.Ticks;

    /// <summary>Calculates a checked time operation.</summary>
    /// <param name="left">The left.</param>
    /// <param name="right">The right.</param>
    /// <returns>The resulting time value.</returns>
    public static bool operator >=(TimePoint left, TimePoint right) => left.Ticks >= right.Ticks;

    /// <summary>Performs from ticks.</summary>
    /// <param name="ticks">The ticks.</param>
    /// <returns>The computed result.</returns>
    public static TimePoint FromTicks(long ticks) => new(ticks);

    /// <summary>Performs compare to.</summary>
    /// <param name="other">The other.</param>
    /// <returns>The computed result.</returns>
    public int CompareTo(TimePoint other) => Ticks.CompareTo(other.Ticks);
}
