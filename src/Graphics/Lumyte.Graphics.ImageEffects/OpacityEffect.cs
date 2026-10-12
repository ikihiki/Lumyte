using RenderGraphBuilder = Lumyte.Graphics.RenderGraph.RenderGraph;

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

        if (!float.IsFinite(this.Opacity) || this.Opacity < 0 || this.Opacity > 1)
        {
            throw new ArgumentOutOfRangeException(nameof(inputs));
        }

        return EffectGpuPass.Append(context, plan, input, input, input.Bounds, new(3, 0, 0, this.Opacity));
    }
}
