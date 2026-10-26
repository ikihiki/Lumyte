using RenderGraphBuilder = Lumyte.Graphics.RenderGraph.RenderGraph;

namespace Lumyte.Graphics.ImageEffects;

/// <summary>Binds shared shaders to a graph and retains each preparation before passes are appended.</summary>
public sealed class ImageEffectsGraphContext
{
    private readonly RenderGraphBuilder _graph;
    private readonly ImageEffectShaders? _shaders;

    /// <summary>Initializes a new instance of the <see cref="ImageEffectsGraphContext"/> class.</summary>
    /// <param name="graph">The graph owning preparation and compiled-plan lifetimes.</param>
    /// <param name="shaders">Borrowed shaders retained by the graph; null selects embedded defaults.</param>
    public ImageEffectsGraphContext(RenderGraphBuilder graph, ImageEffectShaders? shaders = null)
    {
        if (graph.Device is null)
        {
            throw new InvalidOperationException("Image-effect passes require a RenderGraph with a graphics device.");
        }

        _graph = graph;
        _shaders = shaders;
    }

    /// <summary>Appends one effect graph and leaves all partial or completed preparation with the graph.</summary>
    /// <param name="effects">The immutable effect graph.</param>
    /// <param name="source">The graph-local source image.</param>
    /// <returns>The graph-local output.</returns>
    public EffectImage AppendEffects(ImageEffectGraph effects, EffectImage source)
    {
        var context = new EffectPreparationContext(_graph.Device!, source.ColorSemantic, _shaders);
        _graph.Retain(context);
        return _graph.AppendEffectsWithContext(effects, source, context);
    }

    /// <summary>Appends one effect and transfers its fixed resources to the graph before preparation starts.</summary>
    /// <param name="effect">The immutable effect definition.</param>
    /// <param name="inputs">The ordered graph-local inputs.</param>
    /// <returns>The graph-local output.</returns>
    public EffectImage AppendEffect(ImageEffectDefinition effect, params EffectImage[] inputs)
    {
        ArgumentNullException.ThrowIfNull(inputs);
        EffectColorSemantic semantic = inputs.Length == 0 ? EffectColorSemantic.DisplayLinear : inputs[0].ColorSemantic;
        var context = new EffectPreparationContext(_graph.Device!, semantic, _shaders);
        _graph.Retain(context);
        return _graph.AppendEffectWithContext(effect, context, inputs);
    }
}
