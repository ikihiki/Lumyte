using Lumyte.Core.Time;

namespace Lumyte.Animation;

internal static class AnimationSourceValidation
{
    internal static void Duration(Duration duration) => ArgumentOutOfRangeException.ThrowIfNegativeOrZero(duration.Ticks, nameof(duration));

    internal static void Time(Duration time, Duration duration)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(time.Ticks, nameof(time));
        ArgumentOutOfRangeException.ThrowIfGreaterThan(time.Ticks, duration.Ticks, nameof(time));
    }
}
