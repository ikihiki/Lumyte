namespace Lumyte.Animation;

internal abstract class TimelineNode(long length)
{
    internal long Length { get; } = length;

    internal abstract long ValueCount { get; }

    internal abstract bool HasEvents { get; }

    internal abstract bool HasRelease { get; }

    internal abstract void Sample(long time, AnimationOutput output, bool backwards, long priority);

    internal abstract void Endpoints(long from, long to, AnimationOutput output, long priority);

    internal abstract void Events(EventQuery query, EventCollector collector);
}
