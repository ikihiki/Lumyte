namespace Lumyte.UI.Automation;

/// <summary>Defines ui automation page.</summary>
public sealed partial class UiAutomationPage
{
    /// <summary>Capture Ime Candidates Async.</summary>
    /// <param name = "cancellationToken">The cancellation token.</param>
    /// <returns>The operation result.</returns>
    public Task<UiImeCandidateSnapshot?> CaptureImeCandidatesAsync(CancellationToken cancellationToken = default) => ExecuteAsync(
 async run =>
    {
        IUiAutomationImeHost host = RequireImeHost(run);
        await run.SnapshotAsync();
        return await run.Host.RunOnUiThreadAsync(surface => host.CaptureImeCandidates(surface), run.Token);
    },
 null,
 cancellationToken);

    /// <summary>Select Ime Candidate Async.</summary>
    /// <param name = "token">The token.</param>
    /// <param name = "index">The index.</param>
    /// <param name = "cancellationToken">The cancellation token.</param>
    /// <returns>The operation result.</returns>
    public Task SelectImeCandidateAsync(UiImeCandidateToken token, int index, CancellationToken cancellationToken = default) => ExecuteImeAsync(new(token, "select", index), cancellationToken);

    /// <summary>Change Ime Candidate Page Async.</summary>
    /// <param name = "token">The token.</param>
    /// <param name = "page">The page.</param>
    /// <param name = "cancellationToken">The cancellation token.</param>
    /// <returns>The operation result.</returns>
    public Task ChangeImeCandidatePageAsync(UiImeCandidateToken token, int page, CancellationToken cancellationToken = default) => ExecuteImeAsync(new(token, "page", page), cancellationToken);

    /// <summary>Finalize Ime Candidate Async.</summary>
    /// <param name = "token">The token.</param>
    /// <param name = "cancellationToken">The cancellation token.</param>
    /// <returns>The operation result.</returns>
    public Task FinalizeImeCandidateAsync(UiImeCandidateToken token, CancellationToken cancellationToken = default) => ExecuteImeAsync(new(token, "finalize"), cancellationToken);

    /// <summary>Abort Ime Candidates Async.</summary>
    /// <param name = "token">The token.</param>
    /// <param name = "cancellationToken">The cancellation token.</param>
    /// <returns>The operation result.</returns>
    public Task AbortImeCandidatesAsync(UiImeCandidateToken token, CancellationToken cancellationToken = default) => ExecuteImeAsync(new(token, "abort"), cancellationToken);

    private static IUiAutomationImeHost RequireImeHost(UiAutomationRun run) => run.Host as IUiAutomationImeHost ?? throw run.Failure(UiAutomationFailureKind.UnsupportedAction, "The host does not provide candidate operations.");

    private Task ExecuteImeAsync(UiImeCandidateCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command.Token);
        if (command.Token.SessionId == Guid.Empty || command.Token.SessionGeneration == 0 || command.Token.ElementGeneration == 0 || command.Token.Revision == 0 || command.Index < 0)
        {
            throw new ArgumentException("A current candidate token and valid index are required.", nameof(command));
        }

        return ExecuteAsync(
 async run =>
        {
            IUiAutomationImeHost host = RequireImeHost(run);
            await run.SnapshotAsync();
            return await run.WithInputAsync(
 async _ =>
            {
                UiImeCandidateResult result = await host.ExecuteImeCandidateAsync(command, run.Token);
                if (!result.Accepted)
                {
                    throw run.Failure(UiAutomationFailureKind.ActionFailed, result.Code);
                }

                return true;
            },
 token => host.AcquireImeCandidateInputAsync(command.Token, token));
        },
 null,
 cancellationToken);
    }
}
