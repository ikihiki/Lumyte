namespace Lumyte.Animation;

internal sealed class RepeatNode(TimelineNode child, int count) : TimelineNode(checked(child.Length * count))
{
    internal override bool HasEvents { get; } = child.HasEvents;

    internal override bool HasRelease { get; } = child.HasRelease;

    internal override UInt128 ValueCount { get; } = checked(child.ValueCount * (uint)count);

    internal override void Sample(long time, AnimationOutput output, bool backwards, UInt128 priority)
    {
        if (time < 0)
        {
            return;
        }

        long index = time >= Length ? count - 1 : time / child.Length;
        long local = time >= Length ? child.Length : time % child.Length;
        long ordinal = backwards ? count - 1 - index : index;
        child.Sample(local, output, backwards, checked(priority + ((UInt128)ordinal * child.ValueCount)));
    }

    internal override void Endpoints(long from, long to, AnimationOutput output, UInt128 priority)
    {
        if (!HasRelease)
        {
            return;
        }

        long lower = Math.Max(0, Math.Min(from, to));
        long upper = Math.Min(Length, Math.Max(from, to));
        if (lower > upper)
        {
            return;
        }

        long first = Math.Max(0, (lower / child.Length) - 1);
        long last = Math.Min(count - 1, upper / child.Length);

        // Earlier complete cycles produce identical release endpoints.
        if (to >= from)
        {
            first = Math.Max(first, last - 1);
        }
        else
        {
            last = Math.Min(last, first + 1);
        }

        for (long cursor = first; cursor <= last; cursor++)
        {
            long index = to >= from ? cursor : last - (cursor - first);
            long offset = checked(index * child.Length);
            long ordinal = to >= from ? index : count - 1 - index;
            child.Endpoints(from - offset, to - offset, output, checked(priority + ((UInt128)ordinal * child.ValueCount)));
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

        long first = Math.Max(0, (bounded.From / child.Length) - 1);
        long last = Math.Min(count - 1, bounded.To / child.Length);
        for (long cursor = first; cursor <= last; cursor++)
        {
            long index = bounded.Direction > 0 ? cursor : last - (cursor - first);
            child.Events(bounded.Offset(checked(index * child.Length)), collector);
        }
    }
}
