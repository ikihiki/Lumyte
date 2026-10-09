using Lumyte.Core.Time;

namespace Lumyte.Animation;

/// <summary>A copied, validated keyframe curve with endpoint holding and binary key lookup.</summary>
/// <typeparam name="T">The value type.</typeparam>
public sealed class AnimationCurve<T> : IAnimationSource<T>
{
    private readonly AnimationKey<T>[] _keys;

    private readonly IAnimationInterpolator<T> _interpolator;

    /// <summary>Initializes a new instance of the <see cref="AnimationCurve{T}"/> class.</summary>
    /// <param name="duration">The duration.</param>
    /// <param name="keys">The keys.</param>
    /// <param name="interpolator">The interpolator.</param>
    public AnimationCurve(Duration duration, IReadOnlyList<AnimationKey<T>> keys, IAnimationInterpolator<T> interpolator)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(duration.Ticks, nameof(duration));
        ArgumentNullException.ThrowIfNull(keys);
        ArgumentNullException.ThrowIfNull(interpolator);
        if (keys.Count == 0)
        {
            throw new ArgumentException("A curve needs a key.", nameof(keys));
        }

        _keys = [.. keys];
        for (int i = 0; i < _keys.Length; i++)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(_keys[i].Time.Ticks, nameof(keys));
            ArgumentOutOfRangeException.ThrowIfGreaterThan(_keys[i].Time.Ticks, duration.Ticks, nameof(keys));
            if (i > 0 && _keys[i].Time <= _keys[i - 1].Time)
            {
                throw new ArgumentException("Key times must increase strictly.", nameof(keys));
            }

            AnimationInterpolators.Validate(_keys[i].Value);
        }

        _interpolator = interpolator;
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
        if (time <= _keys[0].Time)
        {
            return _keys[0].Value;
        }

        if (time >= _keys[^1].Time)
        {
            return _keys[^1].Value;
        }

        int low = 1;
        int high = _keys.Length - 1;
        while (low < high)
        {
            int middle = low + ((high - low) / 2);
            if (_keys[middle].Time <= time)
            {
                low = middle + 1;
            }
            else
            {
                high = middle;
            }
        }

        AnimationKey<T> a = _keys[low - 1];
        AnimationKey<T> b = _keys[low];
        if (time == a.Time)
        {
            return a.Value;
        }

        float t = AnimationInterpolators.GetInteriorAmount(time.Ticks - a.Time.Ticks, b.Time.Ticks - a.Time.Ticks);
        return _interpolator.Interpolate(a.Value, b.Value, t);
    }
}
