using RenderGraphBuilder = Lumyte.Graphics.RenderGraph.RenderGraph;

namespace Lumyte.Graphics.ImageEffects;

/// <summary>Prepares a effect with a renderer bound at graph construction.</summary>
/// <typeparam name="TDefinition">The immutable definition accepted by this renderer.</typeparam>
public interface IImageEffectRenderer<in TDefinition>
    where TDefinition : ImageEffectDefinition
{
    /// <summary>Appends declared GPU passes and returns an image matching the definition's output bounds.</summary>
    /// <param name="context">The borrowed device, color semantic, and fixed-resource owner.</param>
    /// <param name="plan">The caller-owned render graph.</param>
    /// <param name="effect">The immutable effect parameters.</param>
    /// <param name="inputs">Child images in graph construction order. The span is valid only during this call.</param>
    /// <returns>The graph-local output, preserving the requested color semantic.</returns>
    EffectImage AppendPasses(EffectPreparationContext context, RenderGraphBuilder plan, TDefinition effect, ReadOnlySpan<EffectImage> inputs);
}
