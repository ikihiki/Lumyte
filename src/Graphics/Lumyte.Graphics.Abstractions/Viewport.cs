namespace Lumyte.Graphics.Abstractions;

/// <summary>Describes Viewport encoder state.</summary>
/// <param name="X">The X value.</param>
/// <param name="Y">The Y value.</param>
/// <param name="Width">The Width value.</param>
/// <param name="Height">The Height value.</param>
/// <param name="MinDepth">The MinDepth value.</param>
/// <param name="MaxDepth">The MaxDepth value.</param>
public readonly record struct Viewport(float X, float Y, float Width, float Height, float MinDepth, float MaxDepth);
