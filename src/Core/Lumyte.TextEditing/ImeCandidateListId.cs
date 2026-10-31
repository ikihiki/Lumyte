namespace Lumyte.TextEditing;

/// <summary>Defines ime candidate list id.</summary>
/// <param name = "SessionId">The session id.</param>
/// <param name = "SessionGeneration">The session generation.</param>
/// <param name = "ElementGeneration">The element generation.</param>
public readonly record struct ImeCandidateListId(Guid SessionId, ulong SessionGeneration, ulong ElementGeneration);
