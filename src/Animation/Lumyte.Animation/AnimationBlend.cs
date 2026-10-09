using Lumyte.Core.Time;

namespace Lumyte.Animation;

/// <summary>Combines two typed value sources using time-dependent normalized weights.</summary>
/// <typeparam name="T">The value type.</typeparam>
public sealed class AnimationBlend<T> : IAnimationSource<T>
{
    private readonly IAnimationSource<T> _from;
    private readonly IAnimationSource<T> _to;
    private readonly IAnimationSource<float> _weight;
    private readonly IAnimationInterpolator<T> _interpolator;

    /// <summary>Initializes a new instance of the <see cref="AnimationBlend{T}"/> class.</summary>
    /// <param name="from">The source selected by weight zero.</param>
    /// <param name="to">The source selected by weight one.</param>
    /// <param name="weight">The finite [0, 1] weight source.</param>
    /// <param name="interpolator">The type-specific combination rule.</param>
    public AnimationBlend(IAnimationSource<T> from, IAnimationSource<T> to, IAnimationSource<float> weight, IAnimationInterpolator<T> interpolator)
    {
        ArgumentNullException.ThrowIfNull(from);
        ArgumentNullException.ThrowIfNull(to);
        ArgumentNullException.ThrowIfNull(weight);
        ArgumentNullException.ThrowIfNull(interpolator);
        AnimationSourceValidation.Duration(from.Duration);
        if (from.Duration != to.Duration || from.Duration != weight.Duration)
        {
            throw new ArgumentException("Blend sources must have equal positive durations.");
        }

        _from = from;
        _to = to;
        _weight = weight;
        _interpolator = interpolator;
        Duration = from.Duration;
    }

    /// <inheritdoc />
    public Duration Duration { get; }

    /// <inheritdoc />
    public T Sample(Duration time)
    {
        AnimationSourceValidation.Time(time, Duration);
        float weight = _weight.Sample(time);
        AnimationTimings.ValidateAmount(weight);
        if (weight == 0)
        {
            return _from.Sample(time);
        }

        if (weight == 1)
        {
            return _to.Sample(time);
        }

        return _interpolator.Interpolate(_from.Sample(time), _to.Sample(time), weight);
    }
}
