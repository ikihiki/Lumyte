using Lumyte.TextEditing;
using Lumyte.Windowing.Abstractions;

namespace Lumyte.Platform.TextInput.Abstractions;

/// <summary>Defines text input range geometry.</summary>
/// <param name = "Range">The range.</param>
/// <param name = "Bounds">The bounds.</param>
public sealed record TextInputRangeGeometry(TextSpan Range, WindowRect Bounds);
