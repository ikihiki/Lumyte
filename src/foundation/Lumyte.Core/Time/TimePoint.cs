namespace Lumyte.Core.Time;

/// <summary>A point on a monotonic clock. It has no wall-clock meaning.</summary>
public readonly record struct TimePoint : IComparable<TimePoint>
{
    public static TimePoint Zero => default;

    public long Ticks { get; }

    private TimePoint(long ticks) => Ticks = ticks;

    public static TimePoint FromTicks(long ticks) => new(ticks);

    public int CompareTo(TimePoint other) => Ticks.CompareTo(other.Ticks);

    public static Duration operator -(TimePoint left, TimePoint right)
        => Duration.FromTicks(checked(left.Ticks - right.Ticks));

    public static TimePoint operator +(TimePoint point, Duration duration)
        => new(checked(point.Ticks + duration.Ticks));

    public static TimePoint operator -(TimePoint point, Duration duration)
        => new(checked(point.Ticks - duration.Ticks));

    public static bool operator <(TimePoint left, TimePoint right) => left.Ticks < right.Ticks;

    public static bool operator >(TimePoint left, TimePoint right) => left.Ticks > right.Ticks;

    public static bool operator <=(TimePoint left, TimePoint right) => left.Ticks <= right.Ticks;

    public static bool operator >=(TimePoint left, TimePoint right) => left.Ticks >= right.Ticks;
}
