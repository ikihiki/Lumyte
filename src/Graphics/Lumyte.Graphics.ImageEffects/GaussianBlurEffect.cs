namespace Lumyte.Graphics.ImageEffects;

/// <summary>Defines immutable parameters and region mappings for GaussianBlur.</summary>
public sealed record GaussianBlurEffect : ImageEffectDefinition
{
    /// <summary>Initializes a new instance of the <see cref="GaussianBlurEffect"/> class.</summary>
    /// <param name="sigmaX">The finite nonnegative horizontal device-pixel sigma.</param>
    /// <param name="sigmaY">The finite nonnegative vertical device-pixel sigma.</param>
    public GaussianBlurEffect(float sigmaX, float sigmaY)
    {
        _ = ImageEffectGraph.Radius(sigmaX);
        _ = ImageEffectGraph.Radius(sigmaY);
        SigmaX = sigmaX;
        SigmaY = sigmaY;
    }

    /// <summary>Gets the horizontal device-pixel sigma.</summary>
    public float SigmaX { get; }

    /// <summary>Gets the vertical device-pixel sigma.</summary>
    public float SigmaY { get; }

    /// <inheritdoc/>
    public override int InputCount => 1;

    /// <inheritdoc/>
    public override EffectPixelRect GetOutputBounds(ReadOnlySpan<EffectPixelRect> inputs) => inputs[0].Outset(ImageEffectGraph.Radius(SigmaX), ImageEffectGraph.Radius(SigmaY));

    /// <inheritdoc/>
    public override EffectPixelRect GetRequiredInputBounds(int inputIndex, EffectPixelRect output) => output.Outset(ImageEffectGraph.Radius(SigmaX), ImageEffectGraph.Radius(SigmaY));
}
