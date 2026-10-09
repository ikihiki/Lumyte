using Lumyte.Core.Time;

namespace Lumyte.Animation;

internal sealed class ValueNode<T>(IAnimationSource<T> source, AnimationChannel<T> channel, AnimationFillMode fill) : TimelineNode(source.Duration.Ticks)
{
    internal override bool HasEvents { get; } = false;

    internal override bool HasRelease { get; } = fill == AnimationFillMode.Release;

    internal override UInt128 ValueCount { get; } = 1;

    internal override void Sample(long time, AnimationOutput output, bool backwards, UInt128 priority)
    {
        if (time < 0)
        {
            return;
        }

        if (fill == AnimationFillMode.Release && (time > Length || (backwards ? time == 0 : time == Length)))
        {
            return;
        }

        output.Set(channel, source.Sample(Duration.FromTicks(Math.Min(time, Length))), priority);
    }

    internal override void Endpoints(long from, long to, AnimationOutput output, UInt128 priority)
    {
        if (!HasRelease)
        {
            return;
        }

        if (fill != AnimationFillMode.Release)
        {
            return;
        }

        if (from < Length && to >= Length)
        {
            output.Set(channel, source.Sample(Duration.FromTicks(Length)), priority);
        }
        else if (from > 0 && to <= 0)
        {
            output.Set(channel, source.Sample(Duration.Zero), priority);
        }
    }

    internal override void Events(EventQuery query, EventCollector collector)
    {
    }
}
