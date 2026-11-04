namespace Lumyte.UI.Automation;

/// <summary>Defines ui ime candidate token.</summary>
/// <param name = "SessionId">The session id.</param>
/// <param name = "SessionGeneration">The session generation.</param>
/// <param name = "ElementGeneration">The element generation.</param>
/// <param name = "Revision">The revision.</param>
public sealed record UiImeCandidateToken(Guid SessionId, ulong SessionGeneration, ulong ElementGeneration, ulong Revision);
