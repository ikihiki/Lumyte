namespace Lumyte.Graphics.Drawing2D.RenderGraph;

/// <summary>Owns CPU snapshots and fixed resources referenced by one graph recording.</summary>
/// <remarks>Dispose after GPU completion, command disposal, and compiled graph disposal; no wait is performed.</remarks>
public sealed class PreparedDrawingPasses2D : IDisposable
{
    private readonly List<IDisposable> _resources = [];
    private bool _recorded;
    private bool _disposed;
    private IDisposable? _compositionUse;

    internal PreparedDrawingPasses2D()
    {
    }

    internal bool EnforcesWorkingBudget => _resources.OfType<IPreparedWorkingBudget2D>().Any(batch => batch.EnforcesWorkingBudget);

    /// <summary>Releases owned resources in reverse order; failed entries remain owned for a later retry.</summary>
    public void Dispose()
    {
        if (_disposed && _resources.Count == 0 && _compositionUse is null)
        {
            return;
        }

        _disposed = true;
        List<Exception>? failures = null;
        for (int i = _resources.Count - 1; i >= 0; i--)
        {
            try
            {
                _resources[i].Dispose();
                _resources.RemoveAt(i);
            }
            catch (Exception exception)
            {
                (failures ??= []).Add(exception);
            }
        }

        if (failures is not null)
        {
            throw new AggregateException("Failed to release drawing preparation resources.", failures);
        }

        _compositionUse?.Dispose();
        _compositionUse = null;
    }

    internal long WorkingBytes(object excludedOwner) => DrawingWorkingBudget2D.OwnedBytes(_resources.OfType<IPreparedCommandBatch2D>(), excludedOwner);

    internal void HoldComposition(IDisposable? compositionUse) => _compositionUse = compositionUse;

    internal void Own(IDisposable resource)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _resources.Add(resource);
    }

    internal void BeginRecord()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_recorded)
        {
            throw new InvalidOperationException("Prepared drawing callbacks can be recorded only once.");
        }

        _recorded = true;
    }
}
