namespace Lumyte.Graphics.ImageEffects;

/// <summary>Defines immutable parameters and region mappings for Composite.</summary>
public sealed record CompositeEffect : ImageEffectDefinition
{
    /// <summary>Initializes a new instance of the <see cref="CompositeEffect"/> class.</summary>
    /// <param name="mode">The image composition operation.</param>
    /// <param name="sourceOffsetX">The integer horizontal source translation.</param>
    /// <param name="sourceOffsetY">The integer vertical source translation.</param>
    public CompositeEffect(EffectCompositeMode mode, int sourceOffsetX = 0, int sourceOffsetY = 0)
    {
        if (!Enum.IsDefined(mode))
        {
            throw new ArgumentOutOfRangeException(nameof(mode));
        }

        Mode = mode;
        SourceOffsetX = sourceOffsetX;
        SourceOffsetY = sourceOffsetY;
    }

    /// <summary>Gets the image composition operation.</summary>
    public EffectCompositeMode Mode { get; }

    /// <summary>Gets the horizontal source translation.</summary>
    public int SourceOffsetX { get; }

    /// <summary>Gets the vertical source translation.</summary>
    public int SourceOffsetY { get; }

    /// <inheritdoc/>
    public override int InputCount => 2;

    /// <inheritdoc/>
    public override EffectPixelRect GetOutputBounds(ReadOnlySpan<EffectPixelRect> inputs) => inputs[0].Offset(SourceOffsetX, SourceOffsetY).Union(inputs[1]);

    /// <inheritdoc/>
    public override EffectPixelRect GetRequiredInputBounds(int inputIndex, EffectPixelRect output) => inputIndex switch { 0 => output.OffsetInverse(SourceOffsetX, SourceOffsetY), 1 => output, _ => throw new ArgumentOutOfRangeException(nameof(inputIndex)) };
}
