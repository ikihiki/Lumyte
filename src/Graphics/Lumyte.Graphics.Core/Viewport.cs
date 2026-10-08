namespace Lumyte.Graphics;
/// <summary>
/// Defines a viewport in target texels and a normalized depth interval.
/// </summary>
/// <param name="X">The horizontal origin in target texels.</param>
/// <param name="Y">The vertical origin in target texels.</param>
/// <param name="Width">The width in target texels.</param>
/// <param name="Height">The height in target texels.</param>
/// <param name="MinDepth">The minimum normalized depth, at least zero.</param>
/// <param name="MaxDepth">The maximum normalized depth, at most one.</param>
public readonly record struct Viewport(float X, float Y, float Width, float Height, float MinDepth = 0, float MaxDepth = 1);
