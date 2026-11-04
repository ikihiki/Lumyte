namespace Lumyte.TextEditing;

/// <summary>Defines ime candidate state.</summary>
/// <param name = "Id">The id.</param>
/// <param name = "State">The state.</param>
/// <param name = "Code">The code.</param>
public sealed record ImeCandidateState(ImeCandidateListId? Id, ImeCandidatePresentationState State, string Code);
