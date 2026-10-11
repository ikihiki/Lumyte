namespace Lumyte.Platform.Abstractions;

/// <summary>Defines TextSelection.</summary>
/// <param name="Start">The Start value.</param>
/// <param name="Length">The Length value.</param>
/// <param name="ActiveAtStart">The ActiveAtStart value.</param>
public readonly record struct TextSelection(int Start, int Length, bool ActiveAtStart = false);
