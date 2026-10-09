namespace Lumyte.Animation;

internal sealed class ReverseNode(TimelineNode child) : TimelineNode(child.Length)
{
    internal override bool HasEvents { get; } = child.HasEvents;

    internal override bool HasRelease { get; } = child.HasRelease;

    internal override long ValueCount { get; } = child.ValueCount;

    internal override void Sample(long time, AnimationOutput output, bool backwards, long priority)
    {
        if (time < 0)
        {
            return;
        }

        child.Sample(Length - Math.Min(time, Length), output, !backwards, priority);
    }

    internal override void Endpoints(long from, long to, AnimationOutput output, long priority) => child.Endpoints(Length - from, Length - to, output, priority);

    internal override void Events(EventQuery query, EventCollector collector)
    {
        if (!HasEvents)
        {
            return;
        }

        if (!query.Clamp(Length, out EventQuery bounded))
        {
            return;
        }

        child.Events(new EventQuery(Length - bounded.To, Length - bounded.From, bounded.IncludeTo, bounded.IncludeFrom, checked(bounded.Origin + (bounded.Direction * Length)), -bounded.Direction), collector);
    }
}
