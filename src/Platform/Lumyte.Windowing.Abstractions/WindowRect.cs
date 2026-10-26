namespace Lumyte.Windowing.Abstractions;

/// <summary>Defines WindowRect.</summary>
/// <param name="X">The X value.</param>
/// <param name="Y">The Y value.</param>
/// <param name="Width">The Width value.</param>
/// <param name="Height">The Height value.</param>
public readonly record struct WindowRect(float X, float Y, float Width, float Height);
