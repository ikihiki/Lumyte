namespace Lumyte.TextEditing;

/// <summary>Defines text composition snapshot.</summary>
/// <param name = "Id">The id.</param>
/// <param name = "Range">The range.</param>
/// <param name = "Spans">The spans.</param>
public sealed record TextCompositionSnapshot(Guid Id, TextSpan Range, IReadOnlyList<TextCompositionSpan> Spans);
