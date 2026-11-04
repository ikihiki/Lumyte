using Widget = Lumyte.UI.Compose.Definitions.Widget;

namespace Lumyte.UI.Application;

/// <summary>Defines the IUiApplicationViewChange lifecycle boundary; operations begin on the UI owner.</summary>
internal interface IUiApplicationViewChange : IAsyncDisposable
{
    /// <summary>Prepares once without publishing a candidate.</summary>
    /// <param name="root">The root value.</param>
    /// <param name="cancellationToken">The cancellationToken value.</param>
    /// <returns>The operation result or completion.</returns>
    ValueTask PrepareAsync(Widget root, CancellationToken cancellationToken);

    /// <summary>Gates new input and drains accepted operations, IME and popups.</summary>
    /// <param name="originatingInputOperation">The originatingInputOperation value.</param>
    /// <param name="cancellationToken">The cancellationToken value.</param>
    /// <returns>The operation result or completion.</returns>
    ValueTask QuiesceAsync(Guid? originatingInputOperation, CancellationToken cancellationToken);

    /// <summary>Checks prepared generations synchronously without mutations.</summary>
    /// <param name="code">The code value.</param>
    /// <returns>The operation result or completion.</returns>
    bool TryValidateCommit(out string code);

    /// <summary>Publishes prepared state synchronously without allocations or callbacks.</summary>
    /// <param name="metadata">The opaque integration-owned publication metadata.</param>
    void Commit(object metadata);

    /// <summary>Waits for previous GPU use, retaining its surface.</summary>
    /// <param name="cancellationToken">The cancellationToken value.</param>
    /// <returns>The operation result or completion.</returns>
    ValueTask DrainRetiredAsync(CancellationToken cancellationToken);

    /// <summary>Releases the previous binding and surface after content cleanup.</summary>
    /// <param name="cancellationToken">The cancellationToken value.</param>
    /// <returns>The operation result or completion.</returns>
    ValueTask ReleaseRetiredAsync(CancellationToken cancellationToken);

    /// <summary>Waits for candidate GPU use while retaining its surface.</summary>
    /// <param name="cancellationToken">The cancellationToken value.</param>
    /// <returns>The operation result or completion.</returns>
    ValueTask DrainCandidateAsync(CancellationToken cancellationToken);

    /// <summary>Runs awaited content retirement with revocable cleanup dispatch admission.</summary>
    /// <param name="cleanup">The cleanup value.</param>
    /// <param name="cancellationToken">The cancellationToken value.</param>
    /// <returns>The operation result or completion.</returns>
    ValueTask RunContentCleanupAsync(Func<CancellationToken, ValueTask> cleanup, CancellationToken cancellationToken);

    /// <summary>Releases an uncommitted candidate and reopens the old input gate when allowed.</summary>
    /// <param name="cancellationToken">The cancellationToken value.</param>
    /// <returns>The operation result or completion.</returns>
    ValueTask DiscardAsync(CancellationToken cancellationToken);
}
