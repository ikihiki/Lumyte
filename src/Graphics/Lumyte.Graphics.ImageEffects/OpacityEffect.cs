namespace Lumyte.Graphics.ImageEffects;

/// <summary>Defines immutable parameters and region mappings for Opacity.</summary>
public sealed record OpacityEffect : ImageEffectDefinition
{
    /// <summary>Initializes a new instance of the <see cref="OpacityEffect"/> class.</summary>
    /// <param name="opacity">The finite multiplier in [0, 1].</param>
    public OpacityEffect(float opacity)
    {
        if (!float.IsFinite(opacity) || opacity < 0 || opacity > 1)
        {
            throw new ArgumentOutOfRangeException(nameof(opacity));
        }

        Opacity = opacity;
    }

    /// <summary>Gets the completed-image multiplier.</summary>
    public float Opacity { get; }

    /// <inheritdoc/>
    public override int InputCount => 1;

    /// <inheritdoc/>
    public override EffectPixelRect GetOutputBounds(ReadOnlySpan<EffectPixelRect> inputs) => inputs[0];

    /// <inheritdoc/>
    public override EffectPixelRect GetRequiredInputBounds(int inputIndex, EffectPixelRect output) => output;
}
