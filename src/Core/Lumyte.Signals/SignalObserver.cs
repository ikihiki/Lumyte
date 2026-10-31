namespace Lumyte.Signals;

internal abstract class SignalObserver
{
    // This shared empty map is only read, never loaned to an evaluation.
    private static readonly Dictionary<SignalNode, ulong> _emptyReads = [];
    private ulong _lastInvalidation;
    private Dictionary<SignalNode, ulong>? _dependencies;

    internal Dictionary<SignalNode, ulong> Dependencies => _dependencies ?? _emptyReads;

    // All leases and ownership transfers occur while the graph gate is held.
    internal Dictionary<SignalNode, ulong> RentReads() => SignalReadPool.Rent();

    internal void ReturnReads(Dictionary<SignalNode, ulong> reads) => SignalReadPool.Return(reads);

    internal void Replace(Dictionary<SignalNode, ulong> next, bool subscribe)
    {
        Dictionary<SignalNode, ulong> previous = Dependencies;
        if (ReferenceEquals(previous, next))
        {
            return;
        }

        if (subscribe)
        {
            foreach (SignalNode node in next.Keys)
            {
                if (!previous.ContainsKey(node))
                {
                    node.Add(this);
                }
            }

            foreach (SignalNode node in previous.Keys)
            {
                if (!next.ContainsKey(node))
                {
                    node.Remove(this);
                }
            }
        }

        _dependencies = next;
        if (!ReferenceEquals(previous, _emptyReads))
        {
            ReturnReads(previous);
        }
    }

    internal void Subscribe()
    {
        foreach (SignalNode node in Dependencies.Keys)
        {
            node.Add(this);
        }
    }

    internal void Unsubscribe()
    {
        foreach (SignalNode node in Dependencies.Keys)
        {
            node.Remove(this);
        }
    }

    internal void Clear()
    {
        Unsubscribe();
        Dictionary<SignalNode, ulong>? previous = _dependencies;
        _dependencies = null;
        if (previous is not null)
        {
            ReturnReads(previous);
        }
    }

    internal void InvalidateOnce(ulong revision)
    {
        // Gate serializes mutation and revisions never wrap. Mark before descent.
        if (_lastInvalidation != revision)
        {
            _lastInvalidation = revision;
            Invalidate(revision);
        }
    }

    internal abstract void Invalidate(ulong revision);
}
