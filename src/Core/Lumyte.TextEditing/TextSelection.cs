namespace Lumyte.TextEditing;

/// <summary>Defines text selection.</summary>
/// <param name = "Anchor">The anchor.</param>
/// <param name = "Active">The active.</param>
/// <param name = "AnchorAffinity">The anchor affinity.</param>
/// <param name = "ActiveAffinity">The active affinity.</param>
public readonly record struct TextSelection(int Anchor, int Active, TextCaretAffinity AnchorAffinity = TextCaretAffinity.Leading, TextCaretAffinity ActiveAffinity = TextCaretAffinity.Leading);
