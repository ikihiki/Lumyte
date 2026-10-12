using RenderGraphBuilder = Lumyte.Graphics.RenderGraph.RenderGraph;

namespace Lumyte.Graphics.ImageEffects;

/// <summary>Implements Composite using full-resolution fragment passes and graph-owned textures.</summary>
public sealed class DefaultCompositeEffectRenderer : ICompositeEffectRenderer
{
    /// <inheritdoc/>
    EffectImage ICompositeEffectRenderer.AppendPasses(EffectPreparationContext context, RenderGraphBuilder plan, CompositeEffectNode effect, EffectImage source, EffectImage backdrop)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(effect);
        if (!Enum.IsDefined(effect.Mode))
        {
            throw new ArgumentOutOfRangeException(nameof(effect));
        }

        EffectPixelRect bounds = source.Bounds.Offset(effect.SourceOffsetX, effect.SourceOffsetY).Union(backdrop.Bounds);
        return EffectGpuPass.Append(context, plan, source, backdrop, bounds, new(effect.Mode == EffectCompositeMode.SrcOver ? 4 : 5, 0, 0, 0), offsetX: effect.SourceOffsetX, offsetY: effect.SourceOffsetY);
    }
}
