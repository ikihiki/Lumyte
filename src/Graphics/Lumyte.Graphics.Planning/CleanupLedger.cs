namespace Lumyte.Graphics.Planning;

internal sealed class CleanupLedger
{
    private readonly List<IDisposable> _owners = [];

    internal int Count => _owners.Count;

    internal bool Empty => _owners.Count == 0;

    internal void Add(IDisposable owner) => _owners.Add(owner ?? throw new ArgumentNullException(nameof(owner)));

    internal void Cleanup(int attempts)
    {
        var errors = new List<Exception>();
        for (int attempt = 0; attempt < attempts && _owners.Count != 0; attempt++)
        {
            errors.Clear();
            for (int i = _owners.Count - 1; i >= 0; i--)
            {
                try
                {
                    _owners[i].Dispose();
                    _owners.RemoveAt(i);
                }
                catch (Exception e)
                {
                    errors.Add(e);
                    break;
                }
            }
        }

        if (_owners.Count != 0)
        {
            throw new AggregateException("Cleanup budget exhausted; remaining owners are retained for retry.", errors);
        }
    }
}
