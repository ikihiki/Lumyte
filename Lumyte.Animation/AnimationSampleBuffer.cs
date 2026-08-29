using Lumyte.Core.Time;

namespace Lumyte.Animation;

public sealed class AnimationSampleBuffer
{
    private readonly Dictionary<AnimationChannel, IAnimationValueSlot> values;
    private bool hasSample;

    public AnimationSampleBuffer(IAnimationTimeline timeline)
    {
        ArgumentNullException.ThrowIfNull(timeline);
        ChannelList = [.. timeline.Channels];
        values = new(ChannelList.Length);
        foreach (AnimationChannel channel in ChannelList)
        {
            values.Add(channel, channel.CreateValueSlot());
        }

        Timeline = timeline;
    }

    public IAnimationTimeline Timeline { get; private set; }

    public Duration Time { get; private set; }

    internal AnimationChannel[] ChannelList { get; }

    public T Get<T>(AnimationChannel<T> channel)
    {
        ArgumentNullException.ThrowIfNull(channel);
        if (!hasSample || !values.TryGetValue(channel, out IAnimationValueSlot? value))
        {
            throw new ArgumentException(
                "The channel does not belong to this animation sample buffer or has not been sampled.",
                nameof(channel));
        }

        return ((AnimationValueSlot<T>)value).Value;
    }

    internal void Set<T>(AnimationChannel<T> channel, T value)
    {
        if (!values.TryGetValue(channel, out IAnimationValueSlot? slot))
        {
            throw new ArgumentException(
                "The channel does not belong to this animation sample buffer.",
                nameof(channel));
        }

        ((AnimationValueSlot<T>)slot).Value = value;
    }

    internal void Complete(IAnimationTimeline timeline, Duration time)
    {
        Timeline = timeline;
        Time = time;
        hasSample = true;
    }
}
