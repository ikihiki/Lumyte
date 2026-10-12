namespace Lumyte.Graphics.ImageEffects;

/// <summary>Defines immutable custom effect parameters and CPU-only image-region mappings.</summary>
/// <remarks>Implementations must snapshot their parameters and keep all methods deterministic for the lifetime of their graph.</remarks>
public abstract record ImageEffectDefinition
{
    /// <summary>Gets the nonnegative number of child images required by this operation.</summary>
    public abstract int InputCount { get; }

    /// <summary>Calculates the finite, positive output rectangle from child image bounds.</summary>
    /// <param name="inputs">Child bounds in the order supplied to the custom graph factory.</param>
    /// <returns>The declared output rectangle in the common device-pixel grid.</returns>
    public abstract EffectPixelRect GetOutputBounds(ReadOnlySpan<EffectPixelRect> inputs);

    /// <summary>Calculates a conservative child region required to produce the requested output.</summary>
    /// <param name="inputIndex">The zero-based child index.</param>
    /// <param name="output">The requested output rectangle.</param>
    /// <returns>The required child region, or an empty rectangle when that child is not needed.</returns>
    public abstract EffectPixelRect GetRequiredInputBounds(int inputIndex, EffectPixelRect output);
}
