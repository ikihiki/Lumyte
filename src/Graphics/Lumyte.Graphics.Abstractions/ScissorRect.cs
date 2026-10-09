namespace Lumyte.Graphics.Abstractions;

/// <summary>Describes ScissorRect encoder state.</summary>
/// <param name="X">The X value.</param>
/// <param name="Y">The Y value.</param>
/// <param name="Width">The Width value.</param>
/// <param name="Height">The Height value.</param>
public readonly record struct ScissorRect(uint X, uint Y, uint Width, uint Height);
