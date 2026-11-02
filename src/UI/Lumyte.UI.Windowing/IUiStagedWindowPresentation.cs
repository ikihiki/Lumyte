namespace Lumyte.UI.Windowing;

/// <summary>Supports atomic presentation replacement independently of navigation.</summary>
public interface IUiStagedWindowPresentation : IAsyncDisposable
{
    /// <summary>Prepares the candidate privately; the old presentation remains active.</summary>
    /// <param name="cancellationToken">The cancellationToken value.</param>
    /// <returns>The result or asynchronous completion.</returns>
    ValueTask PrepareAsync(CancellationToken cancellationToken);

    /// <summary>Validates connection and geometry without mutations.</summary>
    /// <param name="code">The code value.</param>
    /// <returns>The result or asynchronous completion.</returns>
    bool TryValidateCommit(out string code);

    /// <summary>Exchanges prepared registrations without awaiting or invoking callbacks.</summary>
    /// <returns>The result or asynchronous completion.</returns>
    IUiWindowPresentation Commit();

    /// <summary>Drains candidate GPU use without releasing its surface.</summary>
    /// <param name="cancellationToken">The cancellationToken value.</param>
    /// <returns>The result or asynchronous completion.</returns>
    ValueTask DrainCandidateAsync(CancellationToken cancellationToken);

    /// <summary>Releases an uncommitted candidate without touching the old binding.</summary>
    /// <param name="cancellationToken">The cancellationToken value.</param>
    /// <returns>The result or asynchronous completion.</returns>
    ValueTask DiscardAsync(CancellationToken cancellationToken);
}
