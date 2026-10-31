namespace Lumyte.UI.Automation;

/// <summary>Defines iui automation ime host.</summary>
public interface IUiAutomationImeHost
{
    /// <summary>Acquire Ime Candidate Input Async.</summary>
    /// <param name = "token">The token.</param>
    /// <param name = "cancellationToken">The cancellation token.</param>
    /// <returns>The operation result.</returns>
    ValueTask<IUiAutomationInputLease> AcquireImeCandidateInputAsync(UiImeCandidateToken token, CancellationToken cancellationToken = default);

    /// <summary>Capture Ime Candidates.</summary>
    /// <param name = "surface">The surface.</param>
    /// <returns>The operation result.</returns>
    UiImeCandidateSnapshot? CaptureImeCandidates(UiSurface surface);

    /// <summary>Execute Ime Candidate Async.</summary>
    /// <param name = "command">The command.</param>
    /// <param name = "cancellationToken">The cancellation token.</param>
    /// <returns>The operation result.</returns>
    ValueTask<UiImeCandidateResult> ExecuteImeCandidateAsync(UiImeCandidateCommand command, CancellationToken cancellationToken = default);
}
