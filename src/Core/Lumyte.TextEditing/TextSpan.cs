namespace Lumyte.TextEditing;

/// <summary>Defines text span.</summary>
/// <param name = "Start">The start.</param>
/// <param name = "Length">The length.</param>
public readonly record struct TextSpan(int Start, int Length);
