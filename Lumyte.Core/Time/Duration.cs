namespace Lumyte.Core.Time;

/// <summary>A signed span of monotonic time with <see cref="TimeSpan"/> precision.</summary>
public readonly record struct Duration : IComparable<Duration>
{
    public static Duration Zero => default;

    public long Ticks { get; }

    public double TotalSeconds => Ticks / (double)TimeSpan.TicksPerSecond;

    private Duration(long ticks) => Ticks = ticks;

    public static Duration FromTicks(long ticks) => new(ticks);

    public static Duration FromSeconds(double seconds)
        => new(checked((long)(seconds * TimeSpan.TicksPerSecond)));

    public static Duration FromTimeSpan(TimeSpan value) => new(value.Ticks);

    public TimeSpan ToTimeSpan() => TimeSpan.FromTicks(Ticks);

    public int CompareTo(Duration other) => Ticks.CompareTo(other.Ticks);

    public static Duration operator +(Duration left, Duration right)
        => new(checked(left.Ticks + right.Ticks));

    public static Duration operator -(Duration left, Duration right)
        => new(checked(left.Ticks - right.Ticks));

    public static Duration operator -(Duration value) => new(checked(-value.Ticks));

    public static Duration operator *(Duration value, double scale)
        => new(checked((long)(value.Ticks * scale)));

    public static Duration operator /(Duration value, double divisor)
        => new(checked((long)(value.Ticks / divisor)));

    public static bool operator <(Duration left, Duration right) => left.Ticks < right.Ticks;

    public static bool operator >(Duration left, Duration right) => left.Ticks > right.Ticks;

    public static bool operator <=(Duration left, Duration right) => left.Ticks <= right.Ticks;

    public static bool operator >=(Duration left, Duration right) => left.Ticks >= right.Ticks;
}
