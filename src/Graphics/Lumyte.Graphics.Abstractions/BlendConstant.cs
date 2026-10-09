namespace Lumyte.Graphics.Abstractions;

/// <summary>Describes BlendConstant encoder state.</summary>
/// <param name="Red">The Red value.</param>
/// <param name="Green">The Green value.</param>
/// <param name="Blue">The Blue value.</param>
/// <param name="Alpha">The Alpha value.</param>
public readonly record struct BlendConstant(float Red, float Green, float Blue, float Alpha);
