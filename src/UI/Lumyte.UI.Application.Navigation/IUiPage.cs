using Lumyte.UI.Navigation;
using Widget = Lumyte.UI.Compose.Definitions.Widget;

namespace Lumyte.UI.Application.Navigation;

/// <summary>A DI-owned page with explicit preparation, synchronous UI construction and retryable retirement.</summary>
public interface IUiPage
{
    /// <summary>Gets the borrowed departure guard, if any.</summary>
    IUiPageLeaveGuard? LeaveGuard => null;

    /// <summary>Prepares data before the page is published.</summary>
    /// <param name="context">This activation's borrowed services.</param>
    /// <param name="cancellationToken">Cancels preparation.</param>
    /// <returns>Preparation completion.</returns>
    ValueTask PrepareAsync(UiPageContext context, CancellationToken cancellationToken = default) => ValueTask.CompletedTask;

    /// <summary>Builds one disconnected declaration on the UI owner.</summary>
    /// <param name="context">This activation's borrowed services.</param>
    /// <returns>A new declaration root.</returns>
    Widget Build(UiPageContext context);

    /// <summary>Returns owner-dependent resources before the DI scope is disposed.</summary>
    /// <param name="cancellationToken">Cancels this retirement operation.</param>
    /// <returns>Retirement completion; failed operations may be retried.</returns>
    ValueTask RetireAsync(CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
}
