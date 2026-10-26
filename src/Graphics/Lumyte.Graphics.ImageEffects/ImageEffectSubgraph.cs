using RenderGraphBuilder = Lumyte.Graphics.RenderGraph.RenderGraph;

namespace Lumyte.Graphics.ImageEffects;

/// <summary>A borrowed view of a parent render graph for an effect callback.</summary>
/// <remarks>All passes and fixed resources belong to Graph. Disposing this view only closes further appends.</remarks>
public sealed class ImageEffectSubgraph : IDisposable
{
    private bool _disposed;

    /// <summary>Initializes a new instance of the <see cref="ImageEffectSubgraph"/> class.</summary>
    /// <param name="graph">The parent graph receiving every pass.</param>
    /// <param name="source">The graph-local image supplied to this callback.</param>
    public ImageEffectSubgraph(RenderGraphBuilder graph, EffectImage source)
    {
        ArgumentNullException.ThrowIfNull(graph);
        ImageEffectRenderGraphExtensions.ValidateImage(source);
        Graph = graph;
        Source = source;
    }

    /// <summary>Gets the borrowed parent graph for custom pass and resource declarations.</summary>
    public RenderGraphBuilder Graph { get; }

    /// <summary>Gets this callback's layer or background input image.</summary>
    public EffectImage Source { get; }

    /// <summary>Appends color grading and optional tone mapping through the parent graph.</summary>
    /// <param name="input">The graph-local input.</param>
    /// <param name="exposure">The exposure in stops.</param>
    /// <param name="saturation">The nonnegative saturation multiplier.</param>
    /// <param name="vignette">The vignette strength in [0, 1].</param>
    /// <param name="toneMap">Whether to convert scene-linear input to display-linear output.</param>
    /// <returns>The graph-local output.</returns>
    public EffectImage AppendPostProcess(EffectImage input, float exposure = 0, float saturation = 1, float vignette = 0, bool toneMap = false)
    {
        EnsureAlive();
        return Graph.AppendPostProcess(input, exposure, saturation, vignette, toneMap);
    }

    /// <summary>Appends a blur and retains its fixed resources in the parent graph.</summary>
    /// <param name="input">The graph-local input.</param>
    /// <param name="sigmaX">The horizontal device-pixel sigma.</param>
    /// <param name="sigmaY">The vertical device-pixel sigma.</param>
    /// <returns>The graph-local result.</returns>
    public EffectImage AppendGaussianBlur(EffectImage input, float sigmaX, float sigmaY)
    {
        EnsureAlive();
        return Graph.AppendGaussianBlur(input, sigmaX, sigmaY);
    }

    /// <summary>Appends opacity and retains its fixed resources in the parent graph.</summary>
    /// <param name="input">The graph-local input.</param>
    /// <param name="opacity">The finite multiplier in [0, 1].</param>
    /// <returns>The graph-local result.</returns>
    public EffectImage AppendOpacity(EffectImage input, float opacity)
    {
        EnsureAlive();
        return Graph.AppendOpacity(input, opacity);
    }

    /// <summary>Appends a matrix and retains its fixed resources in the parent graph.</summary>
    /// <param name="input">The graph-local input.</param>
    /// <param name="matrix">The immutable color matrix.</param>
    /// <returns>The graph-local result.</returns>
    public EffectImage AppendColorMatrix(EffectImage input, ColorMatrix4x5 matrix)
    {
        EnsureAlive();
        return Graph.AppendColorMatrix(input, matrix);
    }

    /// <summary>Appends composition and retains its fixed resources in the parent graph.</summary>
    /// <param name="source">The source image.</param>
    /// <param name="backdrop">The backdrop image.</param>
    /// <param name="mode">The composition mode.</param>
    /// <param name="sourceOffsetX">The integer horizontal source offset.</param>
    /// <param name="sourceOffsetY">The integer vertical source offset.</param>
    /// <returns>The graph-local result.</returns>
    public EffectImage AppendComposite(EffectImage source, EffectImage backdrop, EffectCompositeMode mode, int sourceOffsetX = 0, int sourceOffsetY = 0)
    {
        EnsureAlive();
        return Graph.AppendComposite(source, backdrop, mode, sourceOffsetX, sourceOffsetY);
    }

    /// <summary>Appends a new user effect's fixed implementation and retains its resources in the parent graph.</summary>
    /// <param name="effect">The user-defined effect.</param>
    /// <param name="inputs">The ordered graph-local inputs.</param>
    /// <returns>The graph-local result.</returns>
    public EffectImage AppendEffect(ImageEffectDefinition effect, params EffectImage[] inputs)
    {
        EnsureAlive();
        return Graph.AppendEffect(effect, inputs);
    }

    /// <summary>Closes this borrowed view without releasing parent graph resources.</summary>
    public void Dispose() => _disposed = true;

    private void EnsureAlive() => ObjectDisposedException.ThrowIf(_disposed, this);
}
