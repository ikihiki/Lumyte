using Lumyte.Graphics.Abstractions;
using RenderGraphBuilder = Lumyte.Graphics.RenderGraph.RenderGraph;

namespace Lumyte.Graphics.ImageEffects;

/// <summary>Adds image-effect passes to RenderGraph, which manages dependencies, barriers and temporary images.</summary>
public static class ImageEffectRenderGraphExtensions
{
    /// <summary>Appends ColorMatrix passes from parameters and returns the owned output.</summary>
    /// <param name="plan">The caller-owned render graph.</param>
    /// <param name="input">The graph-local input image.</param>
    /// <param name="matrix">The immutable color matrix.</param>
    /// <returns>The output and fixed-resource owner, disposed after GPU completion.</returns>
    public static PreparedImageEffect AppendColorMatrix(this RenderGraphBuilder plan, EffectImage input, ColorMatrix4x5 matrix)
    {
        ArgumentNullException.ThrowIfNull(plan);
        return plan.AppendEffect(new ColorMatrixEffect(matrix), input);
    }

    /// <summary>Appends GaussianBlur passes from parameters and returns the owned output.</summary>
    /// <param name="plan">The caller-owned render graph.</param>
    /// <param name="input">The graph-local input image.</param>
    /// <param name="sigmaX">The finite nonnegative horizontal device-pixel sigma.</param>
    /// <param name="sigmaY">The finite nonnegative vertical device-pixel sigma.</param>
    /// <returns>The output and fixed-resource owner, disposed after GPU completion.</returns>
    public static PreparedImageEffect AppendGaussianBlur(this RenderGraphBuilder plan, EffectImage input, float sigmaX, float sigmaY)
    {
        ArgumentNullException.ThrowIfNull(plan);
        return plan.AppendEffect(new GaussianBlurEffect(sigmaX, sigmaY), input);
    }

    /// <summary>Appends Opacity passes from parameters and returns the owned output.</summary>
    /// <param name="plan">The caller-owned render graph.</param>
    /// <param name="input">The graph-local input image.</param>
    /// <param name="opacity">The finite multiplier in [0, 1].</param>
    /// <returns>The output and fixed-resource owner, disposed after GPU completion.</returns>
    public static PreparedImageEffect AppendOpacity(this RenderGraphBuilder plan, EffectImage input, float opacity)
    {
        ArgumentNullException.ThrowIfNull(plan);
        return plan.AppendEffect(new OpacityEffect(opacity), input);
    }

    /// <summary>Appends Composite passes from parameters and returns the owned output.</summary>
    /// <param name="plan">The caller-owned render graph.</param>
    /// <param name="source">The graph-local source image.</param>
    /// <param name="backdrop">The graph-local backdrop image.</param>
    /// <param name="mode">The composition mode.</param>
    /// <param name="sourceOffsetX">The integer horizontal source translation.</param>
    /// <param name="sourceOffsetY">The integer vertical source translation.</param>
    /// <returns>The output and fixed-resource owner, disposed after GPU completion.</returns>
    public static PreparedImageEffect AppendComposite(this RenderGraphBuilder plan, EffectImage source, EffectImage backdrop, EffectCompositeMode mode, int sourceOffsetX = 0, int sourceOffsetY = 0)
    {
        ArgumentNullException.ThrowIfNull(plan);
        return plan.AppendEffect(new CompositeEffect(mode, sourceOffsetX, sourceOffsetY), source, backdrop);
    }

    /// <summary>Appends the graph once; failure releases new resources and requires discarding the render graph.</summary>
    /// <param name="plan">The caller-owned render graph.</param>
    /// <param name="graph">The immutable effect graph.</param>
    /// <param name="source">The source image, already bound to the same render graph.</param>
    /// <returns>The owner of the result's fixed resources.</returns>
    public static PreparedImageEffect AppendEffects(this RenderGraphBuilder plan, ImageEffectGraph graph, EffectImage source)
        => plan.AppendEffects(graph, source, null);

    /// <summary>Appends the effect graph using caller-supplied shader artifacts.</summary>
    /// <param name="plan">The caller-owned render graph.</param>
    /// <param name="graph">The immutable effect graph.</param>
    /// <param name="source">The graph-local input image.</param>
    /// <param name="shaders">Optional borrowed artifacts; null selects embedded defaults.</param>
    /// <returns>The owner of fixed GPU resources.</returns>
    public static PreparedImageEffect AppendEffects(this RenderGraphBuilder plan, ImageEffectGraph graph, EffectImage source, ImageEffectShaders? shaders)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(graph);
        ValidateImage(source);
        var context = new EffectPreparationContext(RequireDevice(plan), source.ColorSemantic, shaders);
        var results = new Dictionary<ImageEffectNode, EffectImage>(ReferenceEqualityComparer.Instance);
        try
        {
            return new(Visit(graph.Root), context);
        }
        catch
        {
            context.Release();
            throw;
        }

        EffectImage Visit(ImageEffectNode node)
        {
            if (results.TryGetValue(node, out EffectImage result))
            {
                return result;
            }

            if (results.Count >= 4096)
            {
                throw new NotSupportedException("An effect graph may contain at most 4096 distinct operations.");
            }

            result = node switch
            {
                SourceEffectNode => source,
                EffectOperationNode operation => AppendOperation(operation),
                _ => throw new NotSupportedException("Unknown effect operation."),
            };
            ValidateImage(result);
            if (result.ColorSemantic != source.ColorSemantic)
            {
                throw new InvalidOperationException("An effect cannot implicitly change the color semantic.");
            }

            if (results.Count >= 4096)
            {
                throw new NotSupportedException("An effect graph may contain at most 4096 distinct operations.");
            }

            results.Add(node, result);
            return result;
        }

        EffectImage AppendOperation(EffectOperationNode operation)
        {
            var images = new EffectImage[operation.Inputs.Count];
            var bounds = new EffectPixelRect[operation.Inputs.Count];
            for (int i = 0; i < operation.Inputs.Count; i++)
            {
                images[i] = Visit(operation.Inputs[i]);
                bounds[i] = images[i].Bounds;
            }

            EffectPixelRect expected = operation.Definition.GetOutputBounds(bounds);
            if (expected.IsEmpty)
            {
                throw new InvalidOperationException("An effect must declare positive output dimensions.");
            }

            EffectImage result = operation.Definition.AppendPasses(context, plan, images);
            if (result.Bounds != expected)
            {
                throw new InvalidOperationException("An effect must preserve its definition's declared output bounds.");
            }

            return result;
        }
    }

    /// <summary>Appends one existing effect directly, without constructing an effect graph.</summary>
    /// <param name="plan">The caller-owned render graph; discard it if append fails.</param>
    /// <param name="effect">The existing immutable effect with its fixed implementation.</param>
    /// <param name="inputs">Ordered graph-local input images.</param>
    /// <returns>The output and its fixed-resource owner, disposed after GPU completion.</returns>
    public static PreparedImageEffect AppendEffect(this RenderGraphBuilder plan, ImageEffectDefinition effect, params EffectImage[] inputs)
        => plan.AppendEffectWithShaders(effect, null, inputs);

    /// <summary>Appends an effect using caller-supplied shader artifacts.</summary>
    /// <param name="plan">The graph receiving passes.</param>
    /// <param name="effect">The effect definition.</param>
    /// <param name="shaders">Borrowed artifacts retained through GPU completion.</param>
    /// <param name="inputs">The ordered input images.</param>
    /// <returns>The prepared effect owner.</returns>
    public static PreparedImageEffect AppendEffectWithShaders(this RenderGraphBuilder plan, ImageEffectDefinition effect, ImageEffectShaders? shaders, params EffectImage[] inputs)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(effect);
        ArgumentNullException.ThrowIfNull(inputs);
        if (effect.InputCount < 0 || effect.InputCount != inputs.Length)
        {
            throw new ArgumentException("The image count must match the effect input count.", nameof(inputs));
        }

        EffectColorSemantic semantic = inputs.Length == 0 ? EffectColorSemantic.DisplayLinear : inputs[0].ColorSemantic;
        var bounds = new EffectPixelRect[inputs.Length];
        for (int i = 0; i < inputs.Length; i++)
        {
            ValidateImage(inputs[i]);
            if (inputs[i].ColorSemantic != semantic)
            {
                throw new ArgumentException("Effect inputs must use the same color semantic.", nameof(inputs));
            }

            bounds[i] = inputs[i].Bounds;
        }

        EffectPixelRect expected = effect.GetOutputBounds(bounds);
        if (expected.IsEmpty)
        {
            throw new InvalidOperationException("An effect must declare positive output dimensions.");
        }

        var context = new EffectPreparationContext(RequireDevice(plan), semantic, shaders);
        try
        {
            EffectImage output = effect.AppendPasses(context, plan, inputs);
            ValidateImage(output);
            if (output.Bounds != expected || output.ColorSemantic != semantic)
            {
                throw new InvalidOperationException("An effect must preserve its declared bounds and color semantic.");
            }

            return new(output, context);
        }
        catch
        {
            context.Release();
            throw;
        }
    }

    internal static void ValidateImage(EffectImage image)
    {
        ArgumentNullException.ThrowIfNull(image.Texture);
        if (image.Bounds.IsEmpty)
        {
            throw new ArgumentException("Effect images must have positive dimensions.", nameof(image));
        }

        if (!Enum.IsDefined(image.ColorSemantic))
        {
            throw new ArgumentException("Unknown effect color semantic.", nameof(image));
        }
    }

    private static IGraphicDevice RequireDevice(RenderGraphBuilder plan)
        => plan.Device ?? throw new InvalidOperationException("Image-effect passes require a RenderGraph with a graphics device.");
}
