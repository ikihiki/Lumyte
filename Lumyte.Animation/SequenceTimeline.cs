using Lumyte.Core.Time;

namespace Lumyte.Animation;

public sealed class SequenceTimeline : IAnimationTimeline
{
    private readonly IAnimationTimeline[] children;

    public SequenceTimeline(params IReadOnlyList<IAnimationTimeline> children)
    {
        ArgumentNullException.ThrowIfNull(children);
        this.children = [.. children];
        if (this.children.Length == 0)
        {
            throw new ArgumentException("A sequence must contain at least one timeline.", nameof(children));
        }

        ValidateMatchingChannels(this.children);
        Duration = Duration.FromTicks(this.children.Sum(child => child.Duration.Ticks));
        Channels = this.children[0].Channels;
    }

    public Duration Duration { get; }

    public IReadOnlyCollection<AnimationChannel> Channels { get; }

    public AnimationSample Sample(Duration time)
    {
        Duration clamped = Clamp(time);
        Duration offset = Duration.Zero;
        foreach (IAnimationTimeline child in children)
        {
            Duration end = offset + child.Duration;
            if (clamped <= end || ReferenceEquals(child, children[^1]))
            {
                AnimationSample sample = child.Sample(clamped - offset);
                return new AnimationSample(this, clamped, sample.Values);
            }

            offset = end;
        }

        throw new InvalidOperationException("The sequence could not resolve its sample.");
    }

    public void SampleInto(Duration time, AnimationSampleBuffer buffer)
    {
        ArgumentNullException.ThrowIfNull(buffer);
        Duration clamped = Clamp(time);
        Duration offset = Duration.Zero;
        foreach (IAnimationTimeline child in children)
        {
            Duration end = offset + child.Duration;
            if (clamped <= end || ReferenceEquals(child, children[^1]))
            {
                child.SampleInto(clamped - offset, buffer);
                buffer.Complete(this, clamped);
                return;
            }

            offset = end;
        }

        throw new InvalidOperationException("The sequence could not resolve its sample.");
    }

    private Duration Clamp(Duration time)
    {
        if (time < Duration.Zero)
        {
            return Duration.Zero;
        }

        return time > Duration ? Duration : time;
    }

    private static void ValidateMatchingChannels(IReadOnlyList<IAnimationTimeline> timelines)
    {
        HashSet<AnimationChannel> expected = [.. timelines[0].Channels];
        if (timelines.Skip(1).Any(timeline => !expected.SetEquals(timeline.Channels)))
        {
            throw new ArgumentException("Every timeline in a sequence must define the same channels.", nameof(timelines));
        }
    }
}
