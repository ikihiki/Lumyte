using Lumyte.TextEditing;

namespace Lumyte.UI.TextEditing;

/// <summary>Defines ui text change.</summary>
/// <param name = "Text">The text.</param>
/// <param name = "Revision">The revision.</param>
/// <param name = "Origin">The origin.</param>
public sealed record UiTextChange(string Text, ulong Revision, TextEditOrigin Origin);
