namespace Lumyte.TextEditing;

/// <summary>Defines ime candidate item.</summary>
/// <param name = "Index">The index.</param>
/// <param name = "Text">The text.</param>
public sealed record ImeCandidateItem(int Index, string Text);
