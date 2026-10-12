namespace Lumyte.Graphics.ImageEffects;

/// <summary>Multiplies premultiplied RGBA once.</summary>
/// <param name="Opacity">The multiplier in [0, 1].</param>
/// <param name="Input">The input operation.</param>
public sealed record OpacityEffectNode(float Opacity, ImageEffectNode Input) : ImageEffectNode;
