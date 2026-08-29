using Lumyte.Core.Time;

namespace Lumyte.Animation;

public sealed class AnimationSample
{
    private readonly IReadOnlyDictionary<AnimationTrack, object?> values;

    internal AnimationSample(
        AnimationClip clip,
        Duration time,
        IReadOnlyDictionary<AnimationTrack, object?> values)
    {
        Clip = clip;
        Time = time;
        this.values = values;
    }

    public AnimationClip Clip { get; }

    public Duration Time { get; }

    public T Get<T>(AnimationTrack<T> track)
    {
        ArgumentNullException.ThrowIfNull(track);
        if (!values.TryGetValue(track, out var value))
        {
            throw new ArgumentException("The track does not belong to this animation sample.", nameof(track));
        }

        return (T)value!;
    }
}
