namespace Lumyte.Core.Time;

/// <summary>A signed span of monotonic time with <see cref="TimeSpan"/> precision.</summary>
public readonly record struct Duration : IComparable<Duration>
{
    private Duration(long ticks)
    {
        Ticks = ticks;
    }

    /// <summary>Gets the zero.</summary>
    public static Duration Zero => default;

    /// <summary>Gets the ticks.</summary>
    public long Ticks { get; }

    /// <summary>Gets the total seconds.</summary>
    public double TotalSeconds => Ticks / (double)TimeSpan.TicksPerSecond;

    /// <summary>Calculates a checked time operation.</summary>
    /// <param name="left">The left.</param>
    /// <param name="right">The right.</param>
    /// <returns>The resulting time value.</returns>
    public static Duration operator +(Duration left, Duration right) => new(checked(left.Ticks + right.Ticks));

    /// <summary>Calculates a checked time operation.</summary>
    /// <param name="left">The left.</param>
    /// <param name="right">The right.</param>
    /// <returns>The resulting time value.</returns>
    public static Duration operator -(Duration left, Duration right) => new(checked(left.Ticks - right.Ticks));

    /// <summary>Calculates a checked time operation.</summary>
    /// <param name="value">The value.</param>
    /// <returns>The resulting time value.</returns>
    public static Duration operator -(Duration value) => new(checked(-value.Ticks));

    /// <summary>Calculates a checked time operation.</summary>
    /// <param name="value">The value.</param>
    /// <param name="scale">The scale.</param>
    /// <returns>The resulting time value.</returns>
    public static Duration operator *(Duration value, double scale) => new(checked((long)(value.Ticks * scale)));

    /// <summary>Calculates a checked time operation.</summary>
    /// <param name="value">The value.</param>
    /// <param name="divisor">The divisor.</param>
    /// <returns>The resulting time value.</returns>
    public static Duration operator /(Duration value, double divisor) => new(checked((long)(value.Ticks / divisor)));

    /// <summary>Calculates a checked time operation.</summary>
    /// <param name="left">The left.</param>
    /// <param name="right">The right.</param>
    /// <returns>The resulting time value.</returns>
    public static bool operator <(Duration left, Duration right) => left.Ticks < right.Ticks;

    /// <summary>Calculates a checked time operation.</summary>
    /// <param name="left">The left.</param>
    /// <param name="right">The right.</param>
    /// <returns>The resulting time value.</returns>
    public static bool operator >(Duration left, Duration right) => left.Ticks > right.Ticks;

    /// <summary>Calculates a checked time operation.</summary>
    /// <param name="left">The left.</param>
    /// <param name="right">The right.</param>
    /// <returns>The resulting time value.</returns>
    public static bool operator <=(Duration left, Duration right) => left.Ticks <= right.Ticks;

    /// <summary>Calculates a checked time operation.</summary>
    /// <param name="left">The left.</param>
    /// <param name="right">The right.</param>
    /// <returns>The resulting time value.</returns>
    public static bool operator >=(Duration left, Duration right) => left.Ticks >= right.Ticks;

    /// <summary>Performs from ticks.</summary>
    /// <param name="ticks">The ticks.</param>
    /// <returns>The computed result.</returns>
    public static Duration FromTicks(long ticks) => new(ticks);

    /// <summary>Performs from seconds.</summary>
    /// <param name="seconds">The seconds.</param>
    /// <returns>The computed result.</returns>
    public static Duration FromSeconds(double seconds)
    {
        ArgumentOutOfRangeException.ThrowIfNotEqual(double.IsFinite(seconds), true, nameof(seconds));
        return new(checked((long)(seconds * TimeSpan.TicksPerSecond)));
    }

    /// <summary>Performs from time span.</summary>
    /// <param name="value">The value.</param>
    /// <returns>The computed result.</returns>
    public static Duration FromTimeSpan(TimeSpan value) => new(value.Ticks);

    /// <summary>Performs to time span.</summary>
    /// <returns>The computed result.</returns>
    public TimeSpan ToTimeSpan() => TimeSpan.FromTicks(Ticks);

    /// <summary>Performs compare to.</summary>
    /// <param name="other">The other.</param>
    /// <returns>The computed result.</returns>
    public int CompareTo(Duration other) => Ticks.CompareTo(other.Ticks);
}
