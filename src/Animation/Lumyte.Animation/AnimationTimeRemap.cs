using Lumyte.Core.Time;

namespace Lumyte.Animation;

/// <summary>Samples a value source at time supplied by another immutable source.</summary>
/// <typeparam name="T">The sampled value type.</typeparam>
public sealed class AnimationTimeRemap<T> : IAnimationSource<T>, IContextualAnimationSource<T>
{
    private readonly IAnimationSource<T> _source;
    private readonly IAnimationSource<Duration> _timeMap;

    /// <summary>Initializes a new instance of the <see cref="AnimationTimeRemap{T}"/> class.</summary>
    /// <param name="source">The source to sample at mapped times.</param>
    /// <param name="timeMap">The source producing child-local times.</param>
    public AnimationTimeRemap(IAnimationSource<T> source, IAnimationSource<Duration> timeMap)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(timeMap);
        AnimationSourceValidation.Duration(source.Duration);
        AnimationSourceValidation.Duration(timeMap.Duration);
        _source = source;
        _timeMap = timeMap;
        Duration = timeMap.Duration;
    }

    /// <inheritdoc />
    public Duration Duration { get; }

    /// <inheritdoc />
    public T Sample(Duration time)
    {
        AnimationSourceValidation.Time(time, Duration);
        Duration mapped = _timeMap.Sample(time);
        AnimationSourceValidation.Time(mapped, _source.Duration);
        return _source.Sample(mapped);
    }

    /// <inheritdoc />
    T IContextualAnimationSource<T>.Sample(Duration time, AnimationSourceEvaluationContext context)
    {
        AnimationSourceValidation.Time(time, Duration);
        Duration mapped = context.Evaluate(_timeMap, time);
        AnimationSourceValidation.Time(mapped, _source.Duration);
        return context.Evaluate(_source, mapped);
    }
}
