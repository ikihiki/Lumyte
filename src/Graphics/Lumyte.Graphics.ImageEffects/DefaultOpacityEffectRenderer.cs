using RenderGraphBuilder = Lumyte.Graphics.RenderGraph.RenderGraph;

namespace Lumyte.Graphics.ImageEffects;

/// <summary>Implements Opacity using full-resolution fragment passes and graph-owned textures.</summary>
public sealed class DefaultOpacityEffectRenderer : IOpacityEffectRenderer
{
    /// <inheritdoc/>
    EffectImage IOpacityEffectRenderer.AppendPasses(EffectPreparationContext context, RenderGraphBuilder plan, OpacityEffectNode effect, EffectImage input)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(effect);
        if (!float.IsFinite(effect.Opacity) || effect.Opacity < 0 || effect.Opacity > 1)
        {
            throw new ArgumentOutOfRangeException(nameof(effect));
        }

        return EffectGpuPass.Append(context, plan, input, input, input.Bounds, new(3, 0, 0, effect.Opacity));
    }
}
