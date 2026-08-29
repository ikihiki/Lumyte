using Lumyte.Core.Time;

namespace Lumyte.Animation;

public sealed class ParallelTimeline : IAnimationTimeline
{
    private readonly IAnimationTimeline[] children;

    public ParallelTimeline(params IReadOnlyList<IAnimationTimeline> children)
    {
        ArgumentNullException.ThrowIfNull(children);
        this.children = [.. children];
        if (this.children.Length == 0)
        {
            throw new ArgumentException("A parallel timeline must contain at least one child.", nameof(children));
        }

        AnimationChannel[] channels = [.. this.children.SelectMany(child => child.Channels)];
        if (channels.Distinct().Count() != channels.Length)
        {
            throw new ArgumentException("Parallel timelines cannot write the same channel.", nameof(children));
        }

        Channels = channels;
        Duration = this.children.Max(child => child.Duration);
    }

    public Duration Duration { get; }

    public IReadOnlyCollection<AnimationChannel> Channels { get; }

    public AnimationSample Sample(Duration time)
    {
        Duration clamped = time < Duration.Zero ? Duration.Zero : time > Duration ? Duration : time;
        var values = new Dictionary<AnimationChannel, object?>();
        foreach (IAnimationTimeline child in children)
        {
            Duration childTime = clamped > child.Duration ? child.Duration : clamped;
            foreach ((AnimationChannel channel, object? value) in child.Sample(childTime).Values)
            {
                values.Add(channel, value);
            }
        }

        return new AnimationSample(this, clamped, values);
    }
}
