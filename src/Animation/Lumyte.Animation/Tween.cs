using Lumyte.Core.Time;

namespace Lumyte.Animation;

/// <summary>A pure interpolation between explicit endpoints using a positive typed duration.</summary>
/// <typeparam name="T">The value type.</typeparam>
public sealed class Tween<T> : IAnimationSource<T>
{
    private readonly T _from;

    private readonly T _to;

    private readonly IAnimationInterpolator<T> _interpolator;

    private readonly AnimationEasing _easing;

    /// <summary>Initializes a new instance of the <see cref="Tween{T}"/> class.</summary>
    /// <param name="from">The from.</param>
    /// <param name="to">The to.</param>
    /// <param name="duration">The duration.</param>
    /// <param name="interpolator">The interpolator.</param>
    /// <param name="easing">The easing.</param>
    public Tween(T from, T to, Duration duration, IAnimationInterpolator<T> interpolator, AnimationEasing easing)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(duration.Ticks, nameof(duration));
        ArgumentNullException.ThrowIfNull(interpolator);
        if (!Enum.IsDefined(easing))
        {
            throw new ArgumentOutOfRangeException(nameof(easing));
        }

        AnimationInterpolators.Validate(from);
        AnimationInterpolators.Validate(to);
        _from = from;
        _to = to;
        _interpolator = interpolator;
        _easing = easing;
        Duration = duration;
    }

    /// <summary>Gets the duration.</summary>
    public Duration Duration { get; }

    /// <summary>Computes a value at a local time within the source duration without side effects.</summary>
    /// <param name="time">The time.</param>
    /// <returns>The computed result.</returns>
    public T Sample(Duration time)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(time.Ticks, nameof(time));
        ArgumentOutOfRangeException.ThrowIfGreaterThan(time.Ticks, Duration.Ticks, nameof(time));
        if (time == Duration.Zero)
        {
            return _from;
        }

        if (time == Duration)
        {
            return _to;
        }

        float t = (float)((double)time.Ticks / Duration.Ticks);
        t = _easing switch
        {
            AnimationEasing.EaseIn => t * t,
            AnimationEasing.EaseOut => 1 - ((1 - t) * (1 - t)),
            AnimationEasing.EaseInOut => t < 0.5f ? 2 * t * t : 1 - (2 * (1 - t) * (1 - t)),
            _ => t,
        };
        return _interpolator.Interpolate(_from, _to, t);
    }
}
