namespace Lumyte.Graphics.ImageEffects;

/// <summary>Applies a separable Gaussian kernel in full-resolution device pixels.</summary>
/// <param name="SigmaX">The horizontal standard deviation.</param>
/// <param name="SigmaY">The vertical standard deviation.</param>
/// <param name="Input">The input operation.</param>
public sealed record GaussianBlurEffectNode(float SigmaX, float SigmaY, ImageEffectNode Input) : ImageEffectNode;
