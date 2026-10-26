namespace Lumyte.Graphics.Drawing2D;

/// <summary>A straight-alpha sRGB color whose components must be finite and between zero and one.</summary>
/// <param name="R">The R component.</param>
/// <param name="G">The G component.</param>
/// <param name="B">The B component.</param>
/// <param name="A">The A component.</param>
public readonly record struct Color2D(float R, float G, float B, float A);
