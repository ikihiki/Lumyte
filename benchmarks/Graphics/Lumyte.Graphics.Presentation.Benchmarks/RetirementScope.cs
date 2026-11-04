namespace Lumyte.Graphics.Presentation.Benchmarks;

// Failed/unknown native submission acceptance is not authority to destroy child owners.
internal sealed class RetirementScope : IDisposable
{
    private static readonly List<object> _quarantined = [];
    private readonly List<IDisposable> _owners = [];
    private bool _uncertain;

    internal RetirementScope? Parent { get; init; }

    public void Dispose()
    {
        if (_uncertain)
        {
            return;
        }

        for (int i = _owners.Count - 1; i >= 0; i--)
        {
            try
            {
                _owners[i].Dispose();
                _owners.RemoveAt(i);
            }
            catch
            {
                // Do not continue releasing lower owners (especially the device) after failed cleanup.
                Quarantine();
                throw;
            }
        }
    }

    internal T Own<T>(T owner)
        where T : IDisposable
    {
        _owners.Add(owner);
        return owner;
    }

    internal void Quarantine(params object[] additionalOwners)
    {
        if (!_uncertain)
        {
            _uncertain = true;
            _quarantined.Add(this);
            _quarantined.AddRange(additionalOwners);
            Parent?.Quarantine();
        }
    }

    internal void Release(IDisposable owner)
    {
        owner.Dispose();
        _owners.Remove(owner);
    }
}
