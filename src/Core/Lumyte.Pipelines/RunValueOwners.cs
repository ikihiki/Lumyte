namespace Lumyte.Pipelines;

internal sealed class RunValueOwners : IDisposable
{
    private readonly object _gate = new();
    private readonly List<IDisposable> _owners = [];
    private readonly HashSet<IDisposable> _known = new(ReferenceEqualityComparer.Instance);
    private bool _closing;

    public void Dispose()
    {
        lock (_gate)
        {
            _closing = true;
            for (int i = _owners.Count - 1; i >= 0; i--)
            {
                _owners[i].Dispose();
                _known.Remove(_owners[i]);
                _owners.RemoveAt(i);
            }
        }
    }

    internal void Add(IDisposable owner)
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_closing, this);
            if (_known.Add(owner))
            {
                _owners.Add(owner);
            }
        }
    }
}
