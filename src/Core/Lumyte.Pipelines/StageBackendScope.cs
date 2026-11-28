namespace Lumyte.Pipelines;

/// <summary>A capability-checked, expiring view for backend declarations, never native device access.</summary>
public sealed class StageBackendScope
{
    private readonly StagePreparationContext _context;

    internal StageBackendScope(StagePreparationContext context)
    {
        _context = context;
    }

    /// <summary>Reads an admitted immutable declaration, including opaque placements and supported recursive bundles.</summary>
    /// <typeparam name="T">The declared payload.</typeparam>
    /// <param name="value">An exact input lease of this invocation.</param>
    /// <returns>The immutable declaration, not CPU contents.</returns>
    public T Read<T>(ValueLease<T> value) => _context.BackendRead(value);

    /// <summary>Publishes one backend declaration under the finite host budget.</summary>
    /// <typeparam name="T">The declared payload.</typeparam>
    /// <param name="value">The immutable declaration.</param>
    /// <returns>The sole invocation output.</returns>
    public ValueLease<T> Publish<T>(T value) => _context.BackendPublish(value);

    /// <summary>Transfers early resource cleanup to the run before publication; failed invocations are cleaned too.</summary>
    /// <param name="owner">An independent cleanup lease.</param>
    public void Own(IDisposable owner) => _context.BackendOwn(owner);
}
