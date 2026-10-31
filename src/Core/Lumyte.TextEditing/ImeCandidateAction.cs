namespace Lumyte.TextEditing;

/// <summary>Defines ime candidate action.</summary>
/// <param name = "Id">The id.</param>
/// <param name = "ExpectedRevision">The expected revision.</param>
/// <param name = "Kind">The kind.</param>
/// <param name = "Index">The index.</param>
public sealed record ImeCandidateAction(ImeCandidateListId Id, ulong ExpectedRevision, ImeCandidateActionKind Kind, int? Index = null);
