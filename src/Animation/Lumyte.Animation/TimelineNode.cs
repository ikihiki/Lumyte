namespace Lumyte.Animation;

internal abstract class TimelineNode(long length)
{
    internal long Length { get; } = length;

    // Virtual registration slots include repetitions without expanding the definition.
    // Wider arithmetic keeps parallel values from restricting valid long-duration repeats.
    internal abstract UInt128 ValueCount { get; }

    internal abstract bool HasEvents { get; }

    internal abstract bool HasRelease { get; }

    internal abstract void Sample(long time, AnimationOutput output, bool backwards, UInt128 priority);

    internal abstract void Endpoints(long from, long to, AnimationOutput output, UInt128 priority);

    internal abstract void Events(EventQuery query, EventCollector collector);
}
