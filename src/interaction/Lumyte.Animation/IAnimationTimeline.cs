using Lumyte.Core.Time;

namespace Lumyte.Animation;

public interface IAnimationTimeline
{
    Duration Duration { get; }

    IReadOnlyCollection<AnimationChannel> Channels { get; }

    AnimationSample Sample(Duration time);

    void SampleInto(Duration time, AnimationSampleBuffer buffer);
}
