using Lumyte.Core.Time;

namespace Lumyte.Animation;

public sealed class AnimationSample
{
    private readonly IReadOnlyDictionary<AnimationChannel, object?> values;

    internal AnimationSample(
        IAnimationTimeline timeline,
        Duration time,
        IReadOnlyDictionary<AnimationChannel, object?> values)
    {
        Timeline = timeline;
        Time = time;
        this.values = values;
    }

    public IAnimationTimeline Timeline { get; }

    public AnimationClip? Clip => Timeline as AnimationClip;

    public Duration Time { get; }

    public T Get<T>(AnimationTrack<T> track)
    {
        ArgumentNullException.ThrowIfNull(track);
        return Get(track.Channel);
    }

    public T Get<T>(AnimationChannel<T> channel)
    {
        ArgumentNullException.ThrowIfNull(channel);
        if (!values.TryGetValue(channel, out var value))
        {
            throw new ArgumentException("The channel does not belong to this animation sample.", nameof(channel));
        }

        return (T)value!;
    }

    internal object? GetObject(AnimationChannel channel) => values[channel];

    internal IReadOnlyDictionary<AnimationChannel, object?> Values => values;
}
