namespace Lumyte.TextEditing;

/// <summary>Defines text composition span.</summary>
/// <param name = "Range">The range.</param>
/// <param name = "Underline">The underline.</param>
/// <param name = "Selected">The selected.</param>
public sealed record TextCompositionSpan(TextSpan Range, TextUnderline Underline, bool Selected);
