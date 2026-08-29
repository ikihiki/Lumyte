using Lumyte.Core.Time;

namespace Lumyte.Animation;

public sealed class CrossfadeTimeline : IAnimationTimeline
{
    private readonly AnimationSample from;
    private readonly IAnimationTimeline to;
    private readonly AnimationBlend blend;
    private readonly ICurve curve;

    public CrossfadeTimeline(
        AnimationSample from,
        IAnimationTimeline to,
        Duration duration,
        AnimationBlend blend,
        ICurve? curve = null)
    {
        ArgumentNullException.ThrowIfNull(from);
        ArgumentNullException.ThrowIfNull(to);
        ArgumentNullException.ThrowIfNull(blend);
        if (duration <= Duration.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(duration), "Crossfade duration must be positive.");
        }

        HashSet<AnimationChannel> channels = [.. from.Timeline.Channels];
        if (!channels.SetEquals(to.Channels))
        {
            throw new ArgumentException("Crossfade timelines must define the same channels.", nameof(to));
        }

        this.from = from;
        this.to = to;
        this.blend = blend;
        this.curve = curve ?? Curves.Linear;
        Duration = duration;
        Channels = to.Channels;
    }

    public Duration Duration { get; }

    public IReadOnlyCollection<AnimationChannel> Channels { get; }

    public AnimationSample Sample(Duration time)
    {
        Duration clamped = time < Duration.Zero ? Duration.Zero : time > Duration ? Duration : time;
        float progress = curve.Transform((float)clamped.Ticks / Duration.Ticks);
        Duration destinationTime = clamped > to.Duration ? to.Duration : clamped;
        AnimationSample destination = to.Sample(destinationTime);
        var values = Channels.ToDictionary(
            channel => channel,
            channel => blend.Interpolate(channel, from.GetObject(channel), destination.GetObject(channel), progress));
        return new AnimationSample(this, clamped, values);
    }
}
