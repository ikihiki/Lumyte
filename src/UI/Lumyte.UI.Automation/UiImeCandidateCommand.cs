namespace Lumyte.UI.Automation;

/// <summary>Defines ui ime candidate command.</summary>
/// <param name = "Token">The token.</param>
/// <param name = "Kind">The kind.</param>
/// <param name = "Index">The index.</param>
public sealed record UiImeCandidateCommand(UiImeCandidateToken Token, string Kind, int? Index = null);
