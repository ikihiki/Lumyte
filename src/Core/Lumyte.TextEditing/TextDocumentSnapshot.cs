namespace Lumyte.TextEditing;

/// <summary>Defines text document snapshot.</summary>
/// <param name = "Revision">The revision.</param>
/// <param name = "Text">The text.</param>
/// <param name = "CommittedText">The committed text.</param>
/// <param name = "Selection">The selection.</param>
/// <param name = "Composition">The composition.</param>
public sealed record TextDocumentSnapshot(ulong Revision, string Text, string CommittedText, TextSelection Selection, TextCompositionSnapshot? Composition);
