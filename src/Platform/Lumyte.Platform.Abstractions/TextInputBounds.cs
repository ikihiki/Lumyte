using Lumyte.Windowing.Abstractions;

namespace Lumyte.Platform.Abstractions;

/// <summary>Defines TextInputBounds.</summary>
/// <param name="Rectangle">The Rectangle value.</param>
/// <param name="IsClipped">The IsClipped value.</param>
public readonly record struct TextInputBounds(WindowRect Rectangle, bool IsClipped);
