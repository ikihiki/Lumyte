using RenderGraphBuilder = Lumyte.Graphics.RenderGraph.RenderGraph;

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

    /// <inheritdoc/>
    public override EffectImage AppendPasses(EffectPreparationContext context, RenderGraphBuilder plan, ReadOnlySpan<EffectImage> inputs)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(plan);
        if (inputs.Length != 1)
        {
            throw new ArgumentException("The effect requires one image.", nameof(inputs));
        }

        EffectImage input = inputs[0];

        ArgumentNullException.ThrowIfNull(this.Matrix);
        return EffectGpuPass.Append(context, plan, input, input, input.Bounds, new(0, 0, 0, 0), this.Matrix);
    }
}
