namespace Lumyte.Graphics.Drawing2D.Pipelines;

/// <summary>Build-owned internal dependency, distinct from processing placement state.</summary>
/// <remarks>Factories return fresh services. The build owns disposal; processors only borrow them.</remarks>
public abstract class PipelineSharedService2D : IDisposable
{
    private readonly object _gate = new();
    private object? _owner;

    /// <summary>Releases owned resources; failed cleanup must remain retryable.</summary>
    public abstract void Dispose();

    internal void Adopt(object owner)
    {
        lock (_gate)
        {
            if (_owner is not null && !ReferenceEquals(_owner, owner))
            {
                throw new InvalidOperationException("A shared service belongs to another build.");
            }

            _owner = owner;
        }
    }

    internal void ReleaseReservation(object owner)
    {
        lock (_gate)
        {
            if (ReferenceEquals(_owner, owner))
            {
                _owner = null;
            }
        }
    }
}
