namespace Lumyte.Signals;

internal abstract class SignalNode
{
    private readonly HashSet<SignalObserver> _observers = [];

    internal ulong Revision { get; set; }

    internal bool Observed => _observers.Count != 0;

    internal void Add(SignalObserver observer)
    {
        if (_observers.Add(observer) && _observers.Count == 1)
        {
            Activate();
        }
    }

    internal void Remove(SignalObserver observer)
    {
        if (_observers.Remove(observer) && _observers.Count == 0)
        {
            Deactivate();
        }
    }

    internal void Changed(ulong revision)
    {
        Revision = revision;

        // The caller holds Gate. Invalidation only marks or queues observers;
        // evaluations and subscription changes occur after this traversal ends.
        foreach (SignalObserver observer in _observers)
        {
            observer.InvalidateOnce(revision);
        }
    }

    internal abstract void ReadTracked();

    protected virtual void Activate()
    {
    }

    protected virtual void Deactivate()
    {
    }
}
