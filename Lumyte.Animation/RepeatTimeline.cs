using Lumyte.Core.Time;

namespace Lumyte.Animation;

public sealed class RepeatTimeline : IAnimationTimeline
{
    private readonly IAnimationTimeline child;

    public RepeatTimeline(IAnimationTimeline child, int count)
    {
        this.child = child ?? throw new ArgumentNullException(nameof(child));
        if (count <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(count), "Repeat count must be positive.");
        }

        Count = count;
        Duration = Duration.FromTicks(checked(child.Duration.Ticks * count));
    }

    public int Count { get; }

    public Duration Duration { get; }

    public IReadOnlyCollection<AnimationChannel> Channels => child.Channels;

    public AnimationSample Sample(Duration time)
    {
        Duration clamped = time < Duration.Zero ? Duration.Zero : time > Duration ? Duration : time;
        Duration childTime;
        if (child.Duration == Duration.Zero || clamped == Duration)
        {
            childTime = child.Duration;
        }
        else
        {
            childTime = Duration.FromTicks(clamped.Ticks % child.Duration.Ticks);
        }

        return new AnimationSample(this, clamped, child.Sample(childTime).Values);
    }
}
