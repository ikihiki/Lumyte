namespace Lumyte.TextEditing;

/// <summary>Defines text document change.</summary>
/// <param name = "Revision">The revision.</param>
/// <param name = "Origin">The origin.</param>
/// <param name = "CommittedTextChanged">The committed text changed.</param>
/// <param name = "SelectionChanged">The selection changed.</param>
/// <param name = "CompositionChanged">The composition changed.</param>
public sealed record TextDocumentChange(ulong Revision, TextEditOrigin Origin, bool CommittedTextChanged, bool SelectionChanged, bool CompositionChanged);
