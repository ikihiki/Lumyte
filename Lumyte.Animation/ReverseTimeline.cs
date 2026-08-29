using Lumyte.Core.Time;

namespace Lumyte.Animation;

public sealed class ReverseTimeline(IAnimationTimeline child) : IAnimationTimeline
{
    private readonly IAnimationTimeline child =
        child ?? throw new ArgumentNullException(nameof(child));

    public Duration Duration => child.Duration;

    public IReadOnlyCollection<AnimationChannel> Channels => child.Channels;

    public AnimationSample Sample(Duration time)
    {
        Duration clamped = time < Duration.Zero ? Duration.Zero : time > Duration ? Duration : time;
        return new AnimationSample(this, clamped, child.Sample(Duration - clamped).Values);
    }

    public void SampleInto(Duration time, AnimationSampleBuffer buffer)
    {
        ArgumentNullException.ThrowIfNull(buffer);
        Duration clamped = time < Duration.Zero ? Duration.Zero : time > Duration ? Duration : time;
        child.SampleInto(Duration - clamped, buffer);
        buffer.Complete(this, clamped);
    }
}
