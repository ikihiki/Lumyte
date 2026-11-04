namespace Lumyte.UI.Navigation;

/// <summary>Defines the IUiPageLeaveGuard lifecycle boundary; operations begin on the UI owner.</summary>
public interface IUiPageLeaveGuard
{
    /// <summary>Checks departure once without blocking the owner.</summary>
    /// <param name="context">The context value.</param>
    /// <param name="cancellationToken">The cancellationToken value.</param>
    /// <returns>The operation result or completion.</returns>
    ValueTask<UiPageLeaveDecision> CanLeaveAsync(UiPageLeaveContext context, CancellationToken cancellationToken);
}
