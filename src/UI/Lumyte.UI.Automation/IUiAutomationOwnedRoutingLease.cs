namespace Lumyte.UI.Automation;

/// <summary>Internal host integration: the lease already routes, tracks and cancels every series.</summary>
internal interface IUiAutomationOwnedRoutingLease : IUiAutomationInputLease
{
    /// <summary>Transfers an owner-thread input guard into the lease's cancellation and release boundary.</summary>
    /// <param name="cleanup">The owned guard.</param>
    void RegisterCleanup(IDisposable cleanup);
}
