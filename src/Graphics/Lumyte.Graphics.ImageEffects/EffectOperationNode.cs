using RenderGraphBuilder = Lumyte.Graphics.RenderGraph.RenderGraph;

namespace Lumyte.Graphics.ImageEffects;

/// <summary>Stores an immutable effect definition, child snapshots, and a bound typed renderer.</summary>
public sealed record EffectOperationNode : ImageEffectNode
{
    private readonly IBoundRenderer _renderer;

    private EffectOperationNode(ImageEffectDefinition definition, ImageEffectNode[] inputs, IBoundRenderer renderer)
    {
        Definition = definition;
        Inputs = Array.AsReadOnly(inputs);
        _renderer = renderer;
    }

    /// <summary>Invokes the renderer captured without runtime type lookup.</summary>
    private interface IBoundRenderer
    {
        /// <summary>Appends passes using the captured definition and renderer.</summary>
        /// <param name="context">The preparation owner.</param>
        /// <param name="plan">The caller-owned graph.</param>
        /// <param name="inputs">The ordered child images.</param>
        /// <returns>The effect output image.</returns>
        EffectImage AppendPasses(EffectPreparationContext context, RenderGraphBuilder plan, ReadOnlySpan<EffectImage> inputs);
    }

    /// <summary>Gets the borrowed immutable definition.</summary>
    public ImageEffectDefinition Definition { get; }

    /// <summary>Gets the copied, read-only child node sequence.</summary>
    public IReadOnlyList<ImageEffectNode> Inputs { get; }

    internal static EffectOperationNode Create<TDefinition>(TDefinition definition, IImageEffectRenderer<TDefinition> renderer, ImageEffectNode[] inputs)
        where TDefinition : ImageEffectDefinition => new(definition, inputs, new BoundRenderer<TDefinition>(definition, renderer));

    internal EffectImage AppendPasses(EffectPreparationContext context, RenderGraphBuilder plan, ReadOnlySpan<EffectImage> inputs) => _renderer.AppendPasses(context, plan, inputs);

    private sealed class BoundRenderer<TDefinition>(TDefinition definition, IImageEffectRenderer<TDefinition> renderer) : IBoundRenderer
        where TDefinition : ImageEffectDefinition
    {
        /// <inheritdoc/>
        public EffectImage AppendPasses(EffectPreparationContext context, RenderGraphBuilder plan, ReadOnlySpan<EffectImage> inputs) => renderer.AppendPasses(context, plan, definition, inputs);
    }
}
