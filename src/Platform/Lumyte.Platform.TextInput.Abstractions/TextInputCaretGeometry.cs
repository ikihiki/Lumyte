using Lumyte.TextEditing;
using Lumyte.Windowing.Abstractions;

namespace Lumyte.Platform.TextInput.Abstractions;

/// <summary>Defines text input caret geometry.</summary>
/// <param name = "Position">The position.</param>
/// <param name = "Affinity">The affinity.</param>
/// <param name = "Bounds">The bounds.</param>
public sealed record TextInputCaretGeometry(int Position, TextCaretAffinity Affinity, WindowRect Bounds);
