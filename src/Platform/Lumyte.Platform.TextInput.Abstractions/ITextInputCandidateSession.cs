using Lumyte.TextEditing;

namespace Lumyte.Platform.TextInput.Abstractions;

/// <summary>Defines itext input candidate session.</summary>
public interface ITextInputCandidateSession
{
    /// <summary>Gets the candidate support.</summary>
    ImeCandidateSupport CandidateSupport { get; }

    /// <summary>Gets the candidate state.</summary>
    ImeCandidateState CandidateState { get; }

    /// <summary>Configure Candidate Ui.</summary>
    /// <param name = "policy">The policy.</param>
    /// <param name = "prepared">The prepared.</param>
    void ConfigureCandidateUi(ImeCandidateUiPolicy policy, bool prepared);

    /// <summary>Capture Candidates.</summary>
    /// <returns>The operation result.</returns>
    ImeCandidateSnapshot? CaptureCandidates();

    /// <summary>Request Candidate Async.</summary>
    /// <param name = "action">The action.</param>
    /// <param name = "cancellationToken">The cancellation token.</param>
    /// <returns>The operation result.</returns>
    ValueTask<ImeCandidateResult> RequestCandidateAsync(ImeCandidateAction action, CancellationToken cancellationToken = default);

    /// <summary>Return Candidates To System Async.</summary>
    /// <param name = "cancellationToken">The cancellation token.</param>
    /// <returns>The operation result.</returns>
    ValueTask ReturnCandidatesToSystemAsync(CancellationToken cancellationToken = default);
}
