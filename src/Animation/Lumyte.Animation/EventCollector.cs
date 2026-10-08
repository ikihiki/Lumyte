using Lumyte.Core.Time;

namespace Lumyte.Animation;

internal sealed class EventCollector
{
    private static readonly IComparer<Pending> _pendingComparer = Comparer<Pending>.Create(static (a, b) =>
    {
        int comparison = a.Position.CompareTo(b.Position);
        return comparison != 0 ? comparison : a.Order.CompareTo(b.Order);
    });

    private readonly List<Pending> _pending = [];

    private long _loopOrigin;

    private long _loop;

    internal void Reset() => _pending.Clear();

    internal void BeginLoop(long loop, long origin)
    {
        _loop = loop;
        _loopOrigin = origin;
    }

    internal void Add(AnimationEvent marker, long position)
    {
        AnimationEvent mapped = marker with
        {
            Time = Duration.FromTicks(position - _loopOrigin),
        };
        _pending.Add(new Pending(mapped, position, _loop, _pending.Count));
    }

    internal void CopyTo(ICollection<AnimationEventOccurrence> destination, AnimationPlayback playback)
    {
        _pending.Sort(_pendingComparer);
        foreach (Pending item in _pending)
        {
            destination.Add(new AnimationEventOccurrence(item.Event, item.Loop, playback.OffsetForPosition(item.Position)));
        }
    }

    private readonly record struct Pending(AnimationEvent Event, long Position, long Loop, int Order);
}
