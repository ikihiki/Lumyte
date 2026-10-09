namespace Lumyte.Animation;

internal sealed class MarkerNode(AnimationEvent marker) : TimelineNode(0)
{
    internal override bool HasEvents { get; } = true;

    internal override bool HasRelease { get; } = false;

    internal override long ValueCount { get; } = 0;

    internal override void Sample(long time, AnimationOutput output, bool backwards, long priority)
    {
    }

    internal override void Endpoints(long from, long to, AnimationOutput output, long priority)
    {
    }

    internal override void Events(EventQuery query, EventCollector collector)
    {
        if (!HasEvents)
        {
            return;
        }

        if (query.Contains(0))
        {
            collector.Add(marker, query.Origin);
        }
    }
}
