using RenderGraphBuilder = Lumyte.Graphics.RenderGraph.RenderGraph;

namespace Lumyte.Graphics.ImageEffects;

/// <summary>Implements ColorMatrix using full-resolution fragment passes and graph-owned textures.</summary>
public sealed class DefaultColorMatrixEffectRenderer : IColorMatrixEffectRenderer
{
    /// <inheritdoc/>
    EffectImage IColorMatrixEffectRenderer.AppendPasses(EffectPreparationContext context, RenderGraphBuilder plan, ColorMatrixEffectNode effect, EffectImage input)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(effect);
        ArgumentNullException.ThrowIfNull(effect.Matrix);
        return EffectGpuPass.Append(context, plan, input, input, input.Bounds, new(0, 0, 0, 0), effect.Matrix);
    }
}
