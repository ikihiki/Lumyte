namespace Lumyte.Graphics.ImageEffects;

/// <summary>Defines immutable parameters and region mappings for ColorMatrix.</summary>
public sealed record ColorMatrixEffect : ImageEffectDefinition
{
    /// <summary>Initializes a new instance of the <see cref="ColorMatrixEffect"/> class.</summary>
    /// <param name="matrix">The immutable straight-linear RGBA matrix.</param>
    public ColorMatrixEffect(ColorMatrix4x5 matrix)
    {
        ArgumentNullException.ThrowIfNull(matrix);
        Matrix = matrix;
    }

    /// <summary>Gets the immutable matrix.</summary>
    public ColorMatrix4x5 Matrix { get; }

    /// <inheritdoc/>
    public override int InputCount => 1;

    /// <inheritdoc/>
    public override EffectPixelRect GetOutputBounds(ReadOnlySpan<EffectPixelRect> inputs) => inputs[0];

    /// <inheritdoc/>
    public override EffectPixelRect GetRequiredInputBounds(int inputIndex, EffectPixelRect output) => output;
}
