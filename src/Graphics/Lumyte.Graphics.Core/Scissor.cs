namespace Lumyte.Graphics;
/// <summary>
/// Defines a scissor rectangle in target texels.
/// </summary>
/// <param name="X">The horizontal origin in target texels.</param>
/// <param name="Y">The vertical origin in target texels.</param>
/// <param name="Width">The width in target texels.</param>
/// <param name="Height">The height in target texels.</param>
public readonly record struct Scissor(uint X, uint Y, uint Width, uint Height);
