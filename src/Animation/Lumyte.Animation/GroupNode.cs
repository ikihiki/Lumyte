namespace Lumyte.Animation;

internal sealed class GroupNode(PlacedNode[] children, long length) : TimelineNode(length)
{
    internal override bool HasEvents { get; } = children.Any(child => child.Node.HasEvents);

    internal override bool HasRelease { get; } = children.Any(child => child.Node.HasRelease);

    internal override UInt128 ValueCount { get; } = children.Aggregate(UInt128.Zero, static (count, child) => checked(count + child.Node.ValueCount));

    internal override void Sample(long time, AnimationOutput output, bool backwards, UInt128 priority)
    {
        if (time < 0)
        {
            return;
        }

        foreach (PlacedNode child in children)
        {
            child.Node.Sample(time - child.Start, output, backwards, priority);
            priority = checked(priority + child.Node.ValueCount);
        }
    }

    internal override void Endpoints(long from, long to, AnimationOutput output, UInt128 priority)
    {
        if (!HasRelease)
        {
            return;
        }

        foreach (PlacedNode child in children)
        {
            child.Node.Endpoints(from - child.Start, to - child.Start, output, priority);
            priority = checked(priority + child.Node.ValueCount);
        }
    }

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

        foreach (PlacedNode child in children)
        {
            child.Node.Events(bounded.Offset(child.Start), collector);
        }
    }
}
