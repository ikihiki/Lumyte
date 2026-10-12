using RenderGraphBuilder = Lumyte.Graphics.RenderGraph.RenderGraph;

namespace Lumyte.Graphics.ImageEffects;

/// <summary>Implements Opacity using full-resolution fragment passes and graph-owned textures.</summary>
public sealed class DefaultOpacityEffectRenderer : IImageEffectRenderer<OpacityEffect>
{
    /// <inheritdoc/>
    public EffectImage AppendPasses(EffectPreparationContext context, RenderGraphBuilder plan, OpacityEffect effect, ReadOnlySpan<EffectImage> inputs)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(effect);
        if (inputs.Length != 1)
        {
            throw new ArgumentException("The effect requires one image.", nameof(inputs));
        }

        EffectImage input = inputs[0];

        if (!float.IsFinite(effect.Opacity) || effect.Opacity < 0 || effect.Opacity > 1)
        {
            throw new ArgumentOutOfRangeException(nameof(effect));
        }

        return EffectGpuPass.Append(context, plan, input, input, input.Bounds, new(3, 0, 0, effect.Opacity));
    }
}
