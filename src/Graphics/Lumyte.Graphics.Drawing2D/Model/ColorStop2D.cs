namespace Lumyte.Graphics.Drawing2D;

/// <summary>A gradient stop; duplicate offsets preserve their insertion order.</summary>
/// <param name="Offset">The finite stop offset, not restricted to zero through one.</param>
/// <param name="Color">The straight-alpha sRGB color.</param>
public readonly record struct ColorStop2D(float Offset, Color2D Color);
