using Lumyte.TextEditing;

namespace Lumyte.UI.TextEditing;

/// <summary>Defines ui text caret.</summary>
/// <param name = "Position">The position.</param>
/// <param name = "Affinity">The affinity.</param>
public readonly record struct UiTextCaret(int Position, TextCaretAffinity Affinity);
