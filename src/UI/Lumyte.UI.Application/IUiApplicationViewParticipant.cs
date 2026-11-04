using Lumyte.UI.Automation;

namespace Lumyte.UI.Application;

/// <summary>Optionally contributes diagnostics to the same staged slot publication.</summary>
internal interface IUiApplicationViewParticipant
{
    /// <summary>Captures the trusted cause on the current owner dispatch.</summary>
    /// <param name="context">The stable window instance.</param>
    /// <returns>The input job identity, or null.</returns>
    Guid? CaptureInputOperation(UiApplicationWindowContext context);

    /// <summary>Reserves a private candidate before quiescence.</summary>
    /// <param name="context">The window instance.</param>
    /// <param name="surface">The candidate surface.</param>
    /// <param name="host">Its fixed automation host.</param>
    /// <returns>The reservation, if diagnostics is active.</returns>
    IUiApplicationViewDiagnosticChange? ReserveViewReplacement(UiApplicationWindowContext context, UiSurface surface, IUiAutomationHost host);
}
