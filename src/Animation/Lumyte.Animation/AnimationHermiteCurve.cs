using Lumyte.Core.Time;

namespace Lumyte.Animation;

/// <summary>A copied Hermite curve with per-second tangents and exact key boundaries.</summary>
/// <typeparam name="T">The value and tangent type.</typeparam>
public sealed class AnimationHermiteCurve<T> : IAnimationSource<T>
{
    private readonly AnimationHermiteKey<T>[] _keys;
    private readonly IAnimationHermiteInterpolator<T> _interpolator;

    /// <summary>Initializes a new instance of the <see cref="AnimationHermiteCurve{T}"/> class.</summary>
    /// <param name="duration">The positive curve duration.</param>
    /// <param name="keys">The keys, including per-second tangents.</param>
    /// <param name="interpolator">The value interpolation implementation.</param>
    public AnimationHermiteCurve(Duration duration, IReadOnlyList<AnimationHermiteKey<T>> keys, IAnimationHermiteInterpolator<T> interpolator)
    {
        AnimationSourceValidation.Duration(duration);
        ArgumentNullException.ThrowIfNull(keys);
        ArgumentNullException.ThrowIfNull(interpolator);
        if (keys.Count == 0)
        {
            throw new ArgumentException("A curve needs a key.", nameof(keys));
        }

        _keys = [.. keys];
        for (int index = 0; index < _keys.Length; index++)
        {
            AnimationHermiteKey<T> key = _keys[index];
            AnimationSourceValidation.Time(key.Time, duration);
            if (index > 0 && key.Time <= _keys[index - 1].Time)
            {
                throw new ArgumentException("Key times must increase strictly.", nameof(keys));
            }

            AnimationInterpolators.Validate(key.Value);
            AnimationInterpolators.Validate(key.IncomingTangent, normalizedRotation: false);
            AnimationInterpolators.Validate(key.OutgoingTangent, normalizedRotation: false);
        }

        Duration = duration;
        _interpolator = interpolator;
    }

    /// <inheritdoc />
    public Duration Duration { get; }

    /// <inheritdoc />
    public T Sample(Duration time)
    {
        AnimationSourceValidation.Time(time, Duration);
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

        ref readonly AnimationHermiteKey<T> a = ref _keys[low - 1];
        ref readonly AnimationHermiteKey<T> b = ref _keys[low];
        if (time == a.Time)
        {
            return a.Value;
        }

        Duration interval = b.Time - a.Time;
        float amount = AnimationInterpolators.GetInteriorAmount(time.Ticks - a.Time.Ticks, interval.Ticks);
        return _interpolator.Interpolate(a.Value, b.Value, a.OutgoingTangent, b.IncomingTangent, interval, amount);
    }
}
