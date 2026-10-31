namespace Lumyte.Signals;

/// <summary>A passive dependency snapshot for host-managed construction, with no active observers.</summary>
public sealed class SignalReadSet
{
    private readonly Dictionary<SignalNode, ulong> _reads;

    internal SignalReadSet(Dictionary<SignalNode, ulong> reads)
    {
        _reads = reads;
    }

    /// <summary>Gets a value indicating whether no signal was read.</summary>
    public bool IsEmpty => _reads.Count == 0;

    /// <summary>Gets a value indicating whether the captured dependencies still describe the current values.</summary>
    public bool IsCurrent
    {
        get
        {
            lock (SignalContext.Gate)
            {
                foreach ((SignalNode node, ulong revision) in _reads)
                {
                    SignalContext.ReadUntracked(node);
                    if (node.Revision != revision)
                    {
                        return false;
                    }
                }

                return true;
            }
        }
    }

    /// <summary>Replays captured dependencies into an observer without rerunning construction.</summary>
    public void Read()
    {
        lock (SignalContext.Gate)
        {
            foreach (SignalNode node in _reads.Keys)
            {
                node.ReadTracked();
            }
        }
    }
}
