using Lumyte.Core.Time;

namespace Lumyte.Animation;

/// <summary>A copied, validated keyframe curve with endpoint holding and binary key lookup.</summary>
/// <typeparam name="T">The value type.</typeparam>
public sealed class AnimationCurve<T> : IAnimationSource<T>
{
    private readonly Key[] _keys;

    private readonly Segment[]? _segments;

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

        AnimationKey<T>[] snapshot = [.. keys];
        _keys = new Key[snapshot.Length];
        bool hasSegments = false;
        for (int i = 0; i < _keys.Length; i++)
        {
            _keys[i] = new Key(snapshot[i].Time, snapshot[i].Value);
            hasSegments |= i < snapshot.Length - 1 && (snapshot[i].Hold || snapshot[i].Interpolator is not null || snapshot[i].Timing is not null);
            ArgumentOutOfRangeException.ThrowIfNegative(_keys[i].Time.Ticks, nameof(keys));
            ArgumentOutOfRangeException.ThrowIfGreaterThan(_keys[i].Time.Ticks, duration.Ticks, nameof(keys));
            if (i > 0 && _keys[i].Time <= _keys[i - 1].Time)
            {
                throw new ArgumentException("Key times must increase strictly.", nameof(keys));
            }

            AnimationInterpolators.Validate(_keys[i].Value);
        }

        if (hasSegments)
        {
            _segments = new Segment[snapshot.Length - 1];
            for (int index = 0; index < _segments.Length; index++)
            {
                _segments[index] = new Segment(snapshot[index].Hold, snapshot[index].Interpolator, snapshot[index].Timing);
            }
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

        ref readonly Key a = ref _keys[low - 1];
        ref readonly Key b = ref _keys[low];
        if (time == a.Time)
        {
            return a.Value;
        }

        float t = AnimationInterpolators.GetInteriorAmount(time.Ticks - a.Time.Ticks, b.Time.Ticks - a.Time.Ticks);
        if (_segments is null)
        {
            return _interpolator.Interpolate(a.Value, b.Value, t);
        }

        ref readonly Segment segment = ref _segments[low - 1];
        if (segment.Hold)
        {
            return a.Value;
        }

        T result = (segment.Interpolator ?? _interpolator).Interpolate(a.Value, b.Value, AnimationTimings.Apply(segment.Timing, t));
        if (!AnimationInterpolators.IsValid(result))
        {
            throw new InvalidOperationException("Segment interpolation produced a non-finite value or an unnormalized rotation.");
        }

        return result;
    }

    private readonly record struct Key(Duration Time, T Value);

    private readonly record struct Segment(bool Hold, IAnimationInterpolator<T>? Interpolator, IAnimationTiming? Timing);
}
