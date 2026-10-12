using RenderGraphBuilder = Lumyte.Graphics.RenderGraph.RenderGraph;

namespace Lumyte.Graphics.ImageEffects;

/// <summary>Implements ColorMatrix using full-resolution fragment passes and graph-owned textures.</summary>
public sealed class DefaultColorMatrixEffectRenderer : IImageEffectRenderer<ColorMatrixEffect>
{
    /// <inheritdoc/>
    public EffectImage AppendPasses(EffectPreparationContext context, RenderGraphBuilder plan, ColorMatrixEffect effect, ReadOnlySpan<EffectImage> inputs)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(effect);
        if (inputs.Length != 1)
        {
            throw new ArgumentException("The effect requires one image.", nameof(inputs));
        }

        EffectImage input = inputs[0];

        ArgumentNullException.ThrowIfNull(effect.Matrix);
        return EffectGpuPass.Append(context, plan, input, input, input.Bounds, new(0, 0, 0, 0), effect.Matrix);
    }
}
