namespace Lumyte.Diagnostics;

/// <summary>A DI-injected owning-thread operation queue.</summary>
/// <typeparam name="TPoint">The execution point marker.</typeparam>
public interface IDiagnosticPump<TPoint>
    where TPoint : class
{
    /// <summary>Gets detached catalogs for active subsystems.</summary>
    IReadOnlyList<DiagnosticSubsystemCatalog> Catalog { get; }

    /// <summary>Activates the catalog on the owner thread.</summary>
    void Activate();

    /// <summary>Queues a request from the trusted diagnostic host.</summary>
    /// <param name="request">The request argument.</param>
    /// <returns>The computed result.</returns>
    Task<DiagnosticOperationResult> SubmitAsync(DiagnosticRequest request);

    /// <summary>Executes queued work on the owner thread.</summary>
    /// <param name="frame">The frame argument.</param>
    /// <param name="budget">The budget argument.</param>
    void Pump(DiagnosticFrame frame, DiagnosticBudget budget);

    /// <summary>Removes the catalog and rejects pending requests.</summary>
    void Deactivate();
}
