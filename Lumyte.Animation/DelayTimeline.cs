using Lumyte.Core.Time;

namespace Lumyte.Animation;

public sealed class DelayTimeline : IAnimationTimeline
{
    private readonly IAnimationTimeline child;
    private readonly Duration delay;

    public DelayTimeline(IAnimationTimeline child, Duration delay)
    {
        this.child = child ?? throw new ArgumentNullException(nameof(child));
        if (delay < Duration.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(delay), "Timeline delay cannot be negative.");
        }

        this.delay = delay;
        Duration = delay + child.Duration;
    }

    public Duration Duration { get; }

    public IReadOnlyCollection<AnimationChannel> Channels => child.Channels;

    public AnimationSample Sample(Duration time)
    {
        Duration clamped = time < Duration.Zero ? Duration.Zero : time > Duration ? Duration : time;
        Duration childTime = clamped <= delay ? Duration.Zero : clamped - delay;
        return new AnimationSample(this, clamped, child.Sample(childTime).Values);
    }
}
