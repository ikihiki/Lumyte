namespace Lumyte.TextEditing;

/// <summary>Defines ime candidate result.</summary>
/// <param name = "Action">The action.</param>
/// <param name = "Accepted">The accepted.</param>
/// <param name = "Code">The code.</param>
public sealed record ImeCandidateResult(ImeCandidateAction Action, bool Accepted, string Code);
