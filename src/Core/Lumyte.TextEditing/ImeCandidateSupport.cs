namespace Lumyte.TextEditing;

/// <summary>Defines ime candidate support.</summary>
/// <param name = "CanReadList">The can read list.</param>
/// <param name = "CanSuppressSystemUi">The can suppress system ui.</param>
/// <param name = "CanRequestBehavior">The can request behavior.</param>
/// <param name = "Code">The code.</param>
public sealed record ImeCandidateSupport(bool CanReadList, bool CanSuppressSystemUi, bool CanRequestBehavior, string Code);
