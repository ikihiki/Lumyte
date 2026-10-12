namespace Lumyte.Graphics.ImageEffects;

/// <summary>Builds immutable, bounded-depth effect DAGs with standard and user-defined operations.</summary>
public sealed class ImageEffectGraph
{
    private readonly int _depth;

    private ImageEffectGraph(ImageEffectNode root, int depth)
    {
        if (depth > 256)
        {
            throw new ArgumentException("Effect graphs support at most 256 nested operations.");
        }

        Root = root;
        _depth = depth;
    }

    /// <summary>Gets the immutable root snapshot.</summary>
    public ImageEffectNode Root { get; }

    /// <summary>Creates a reference to the compiler's source image.</summary>
    /// <returns>The source graph.</returns>
    public static ImageEffectGraph Source() => new(new SourceEffectNode(), 1);

    /// <summary>Transforms straight linear RGBA and premultiplies the result.</summary>
    /// <param name="matrix">The matrix.</param>
    /// <param name="input">The input graph.</param>
    /// <returns>The new graph.</returns>
    public static ImageEffectGraph ColorMatrix(ColorMatrix4x5 matrix, ImageEffectGraph input)
    {
        ArgumentNullException.ThrowIfNull(matrix);
        ArgumentNullException.ThrowIfNull(input);
        return new(new ColorMatrixEffectNode(matrix, input.Root), input._depth + 1);
    }

    /// <summary>Blurs with normalized kernels of radius ceil(3 * sigma).</summary>
    /// <param name="sigmaX">The finite nonnegative horizontal device-pixel sigma.</param>
    /// <param name="sigmaY">The finite nonnegative vertical device-pixel sigma.</param>
    /// <param name="input">The input graph.</param>
    /// <returns>The new graph.</returns>
    public static ImageEffectGraph GaussianBlur(float sigmaX, float sigmaY, ImageEffectGraph input)
    {
        ArgumentNullException.ThrowIfNull(input);
        _ = Radius(sigmaX);
        _ = Radius(sigmaY);
        return new(new GaussianBlurEffectNode(sigmaX, sigmaY, input.Root), input._depth + 1);
    }

    /// <summary>Multiplies a completed image by opacity.</summary>
    /// <param name="opacity">A finite multiplier in [0, 1].</param>
    /// <param name="input">The input graph.</param>
    /// <returns>The new graph.</returns>
    public static ImageEffectGraph Opacity(float opacity, ImageEffectGraph input)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (!float.IsFinite(opacity) || opacity < 0 || opacity > 1)
        {
            throw new ArgumentOutOfRangeException(nameof(opacity));
        }

        return new(new OpacityEffectNode(opacity, input.Root), input._depth + 1);
    }

    /// <summary>Composes graph children over their finite bounding-rectangle union.</summary>
    /// <param name="source">The source graph.</param>
    /// <param name="backdrop">The backdrop graph.</param>
    /// <param name="mode">The composition mode.</param>
    /// <param name="sourceOffsetX">The integer horizontal source translation.</param>
    /// <param name="sourceOffsetY">The integer vertical source translation.</param>
    /// <returns>The new graph.</returns>
    public static ImageEffectGraph Composite(ImageEffectGraph source, ImageEffectGraph backdrop, EffectCompositeMode mode, int sourceOffsetX = 0, int sourceOffsetY = 0)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(backdrop);
        if (!Enum.IsDefined(mode))
        {
            throw new ArgumentOutOfRangeException(nameof(mode));
        }

        return new(new CompositeEffectNode(source.Root, backdrop.Root, mode, sourceOffsetX, sourceOffsetY), Math.Max(source._depth, backdrop._depth) + 1);
    }

    /// <summary>Binds an immutable user definition to its typed renderer and copied child graphs.</summary>
    /// <typeparam name="TDefinition">The immutable user definition.</typeparam>
    /// <param name="definition">CPU-only parameters and region mappings, retained without mutation.</param>
    /// <param name="renderer">The borrowed renderer retained for every preparation of this graph.</param>
    /// <param name="inputs">Child graphs in definition order. Zero inputs are permitted for image generators.</param>
    /// <returns>The new custom graph.</returns>
    public static ImageEffectGraph Custom<TDefinition>(TDefinition definition, IImageEffectRenderer<TDefinition> renderer, params ImageEffectGraph[] inputs)
        where TDefinition : ImageEffectDefinition
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(renderer);
        ArgumentNullException.ThrowIfNull(inputs);
        int inputCount = definition.InputCount;
        if (inputCount < 0 || inputCount != inputs.Length)
        {
            throw new ArgumentException("The child count must match the definition's nonnegative input count.", nameof(inputs));
        }

        var nodes = new ImageEffectNode[inputs.Length];
        int depth = 1;
        for (int i = 0; i < inputs.Length; i++)
        {
            ArgumentNullException.ThrowIfNull(inputs[i]);
            nodes[i] = inputs[i].Root;
            depth = Math.Max(depth, inputs[i]._depth + 1);
        }

        return new(CustomEffectNode.Create(definition, renderer, nodes), depth);
    }

    internal static int Radius(float sigma)
    {
        if (!float.IsFinite(sigma) || sigma < 0 || Math.Ceiling(3d * sigma) > int.MaxValue)
        {
            throw new ArgumentOutOfRangeException(nameof(sigma));
        }

        return (int)Math.Ceiling(3d * sigma);
    }
}
