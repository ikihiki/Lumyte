using Widget = Lumyte.UI.Compose.Definitions.Widget;

namespace Lumyte.UI.Navigation;

/// <summary>Defines the IUiPageActivation lifecycle boundary; operations begin on the UI owner.</summary>
public interface IUiPageActivation : IAsyncDisposable
{
    /// <summary>Gets a value indicating whether preparation completed successfully.</summary>
    bool IsPrepared { get; }

    /// <summary>Gets the borrowed Root value.</summary>
    Widget Root { get; }

    /// <summary>Gets the borrowed LeaveGuard value.</summary>
    IUiPageLeaveGuard? LeaveGuard { get; }

    /// <summary>Prepares once without publishing a candidate.</summary>
    /// <param name="cancellationToken">The cancellationToken value.</param>
    /// <returns>The operation result or completion.</returns>
    ValueTask PrepareAsync(CancellationToken cancellationToken);

    /// <summary>Releases UI-dependent resources, retaining completed stages for retry.</summary>
    /// <param name="cancellationToken">The cancellationToken value.</param>
    /// <returns>The operation result or completion.</returns>
    ValueTask RetireAsync(CancellationToken cancellationToken);
}
