using Lumyte.Graphics.Abstractions;
using RenderGraphBuilder = Lumyte.Graphics.RenderGraph.RenderGraph;

namespace Lumyte.Graphics.ImageEffects;

/// <summary>A borrowed view of a parent render graph that owns fixed resources for an effect callback.</summary>
/// <remarks>All passes and handles belong to Graph. Dispose after GPU completion and compiled-plan disposal.</remarks>
public sealed class ImageEffectSubgraph : IDisposable
{
    private readonly List<PreparedImageEffect> _effects = [];
    private bool _disposed;

    /// <summary>Initializes a new instance of the <see cref="ImageEffectSubgraph"/> class.</summary>
    /// <param name="graph">The parent graph receiving every pass.</param>
    /// <param name="source">The graph-local image supplied to this callback.</param>
    /// <param name="programs">The optional borrowed program provider, frozen for the callback.</param>
    public ImageEffectSubgraph(RenderGraphBuilder graph, EffectImage source, IGraphicsProgramProvider? programs = null)
    {
        ArgumentNullException.ThrowIfNull(graph);
        ImageEffectRenderGraphExtensions.ValidateImage(source);
        Graph = graph;
        Source = source;
        Programs = programs ?? ImageEffectPrograms.DefaultProvider;
    }

    /// <summary>Gets the borrowed parent graph for custom pass and resource declarations.</summary>
    public RenderGraphBuilder Graph { get; }

    /// <summary>Gets this callback's layer or background input image.</summary>
    public EffectImage Source { get; }

    /// <summary>Gets the borrowed program provider for appended effects.</summary>
    public IGraphicsProgramProvider Programs { get; }

    /// <summary>Appends a blur and retains its fixed resources in this scope.</summary>
    /// <param name="input">The graph-local input.</param>
    /// <param name="sigmaX">The horizontal device-pixel sigma.</param>
    /// <param name="sigmaY">The vertical device-pixel sigma.</param>
    /// <returns>The graph-local result.</returns>
    public EffectImage AppendGaussianBlur(EffectImage input, float sigmaX, float sigmaY)
    {
        EnsureAlive();
        return Own(Graph.AppendGaussianBlur(input, sigmaX, sigmaY, Programs));
    }

    /// <summary>Appends opacity and retains its fixed resources in this scope.</summary>
    /// <param name="input">The graph-local input.</param>
    /// <param name="opacity">The finite multiplier in [0, 1].</param>
    /// <returns>The graph-local result.</returns>
    public EffectImage AppendOpacity(EffectImage input, float opacity)
    {
        EnsureAlive();
        return Own(Graph.AppendOpacity(input, opacity, Programs));
    }

    /// <summary>Appends a matrix and retains its fixed resources in this scope.</summary>
    /// <param name="input">The graph-local input.</param>
    /// <param name="matrix">The immutable color matrix.</param>
    /// <returns>The graph-local result.</returns>
    public EffectImage AppendColorMatrix(EffectImage input, ColorMatrix4x5 matrix)
    {
        EnsureAlive();
        return Own(Graph.AppendColorMatrix(input, matrix, Programs));
    }

    /// <summary>Appends composition and retains its fixed resources in this scope.</summary>
    /// <param name="source">The source image.</param>
    /// <param name="backdrop">The backdrop image.</param>
    /// <param name="mode">The composition mode.</param>
    /// <param name="sourceOffsetX">The integer horizontal source offset.</param>
    /// <param name="sourceOffsetY">The integer vertical source offset.</param>
    /// <returns>The graph-local result.</returns>
    public EffectImage AppendComposite(EffectImage source, EffectImage backdrop, EffectCompositeMode mode, int sourceOffsetX = 0, int sourceOffsetY = 0)
    {
        EnsureAlive();
        return Own(Graph.AppendComposite(source, backdrop, mode, sourceOffsetX, sourceOffsetY, Programs));
    }

    /// <summary>Appends a new user effect's fixed implementation and retains its resources.</summary>
    /// <param name="effect">The user-defined effect.</param>
    /// <param name="inputs">The ordered graph-local inputs.</param>
    /// <returns>The graph-local result.</returns>
    public EffectImage AppendEffect(ImageEffectDefinition effect, params EffectImage[] inputs)
    {
        EnsureAlive();
        return Own(Graph.AppendEffect(effect, inputs, Programs));
    }

    /// <summary>Transfers ownership of a custom prepared effect to this scope.</summary>
    /// <param name="effect">The successful append's fixed-resource owner.</param>
    /// <returns>The graph-local result.</returns>
    public EffectImage Own(PreparedImageEffect effect)
    {
        EnsureAlive();
        ArgumentNullException.ThrowIfNull(effect);
        _effects.Add(effect);
        return effect.Output;
    }

    /// <summary>Releases appended effects in reverse order; does not dispose or execute the parent graph.</summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        List<Exception>? errors = null;
        for (int i = _effects.Count - 1; i >= 0; i--)
        {
            try
            {
                _effects[i].Dispose();
            }
            catch (Exception error)
            {
                (errors ??= []).Add(error);
            }
        }

        _effects.Clear();
        if (errors is not null)
        {
            throw new AggregateException("Failed to release effect subgraph resources.", errors);
        }
    }

    private void EnsureAlive() => ObjectDisposedException.ThrowIf(_disposed, this);
}
