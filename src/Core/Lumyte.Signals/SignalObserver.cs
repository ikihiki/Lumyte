namespace Lumyte.Signals;

internal abstract class SignalObserver
{
    internal Dictionary<SignalNode, ulong> Dependencies { get; private set; } = [];

    internal void Replace(Dictionary<SignalNode, ulong> next, bool subscribe)
    {
        if (subscribe)
        {
            foreach (SignalNode node in next.Keys)
            {
                if (!Dependencies.ContainsKey(node))
                {
                    node.Add(this);
                }
            }

            foreach (SignalNode node in Dependencies.Keys)
            {
                if (!next.ContainsKey(node))
                {
                    node.Remove(this);
                }
            }
        }

        Dependencies = next;
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
        Dependencies.Clear();
    }

    internal abstract void Invalidate(ulong revision, HashSet<SignalObserver> visited);
}
