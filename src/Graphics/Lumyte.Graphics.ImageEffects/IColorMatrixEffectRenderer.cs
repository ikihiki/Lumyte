using RenderGraphBuilder = Lumyte.Graphics.RenderGraph.RenderGraph;

namespace Lumyte.Graphics.ImageEffects;

/// <summary>Declares the GPU passes for a ColorMatrix image operation.</summary>
public interface IColorMatrixEffectRenderer
{
    /// <summary>Appends passes and returns the graph-local result image.</summary>
    /// <param name="context">The fixed-resource owner and device.</param>
    /// <param name="plan">The caller-owned render graph.</param>
    /// <param name="effect">The immutable operation.</param>
    /// <param name="input">The input image.</param>
    /// <returns>The result image.</returns>
    EffectImage AppendPasses(EffectPreparationContext context, RenderGraphBuilder plan, ColorMatrixEffectNode effect, EffectImage input);
}
