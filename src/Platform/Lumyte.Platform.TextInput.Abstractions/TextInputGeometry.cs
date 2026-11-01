using Lumyte.Windowing.Abstractions;

namespace Lumyte.Platform.TextInput.Abstractions;

/// <summary>Defines text input geometry.</summary>
/// <param name = "DocumentRevision">The document revision.</param>
/// <param name = "LayoutRevision">The layout revision.</param>
/// <param name = "Viewport">The viewport.</param>
/// <param name = "Caret">The caret.</param>
/// <param name = "Ranges">The ranges.</param>
/// <param name = "CaretStops">The caret stops.</param>
public sealed record TextInputGeometry(ulong DocumentRevision, ulong LayoutRevision, WindowRect Viewport, WindowRect Caret, IReadOnlyList<TextInputRangeGeometry> Ranges, IReadOnlyList<TextInputCaretGeometry> CaretStops);
