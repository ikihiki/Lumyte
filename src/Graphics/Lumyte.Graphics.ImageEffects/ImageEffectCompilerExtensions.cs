namespace Lumyte.Graphics.ImageEffects;

/// <summary>Constructs effect graphs through compiler extension methods without allocating GPU resources.</summary>
public static class ImageEffectCompilerExtensions
{
    /// <summary>Binds any immutable effect definition and typed renderer to its child graphs.</summary>
    /// <typeparam name="TDefinition">The immutable effect definition.</typeparam>
    /// <param name="compiler">The borrowed compiler. GPU passes are added separately by AppendPasses.</param>
    /// <param name="definition">The immutable CPU parameters and region mapping.</param>
    /// <param name="renderer">The renderer directly bound to the graph.</param>
    /// <param name="inputs">The ordered child graphs, copied during construction.</param>
    /// <returns>The reusable, bounded-depth effect graph.</returns>
    public static ImageEffectGraph Effect<TDefinition>(this ImageEffectCompiler compiler, TDefinition definition, IImageEffectRenderer<TDefinition> renderer, params ImageEffectGraph[] inputs)
        where TDefinition : ImageEffectDefinition
    {
        ArgumentNullException.ThrowIfNull(compiler);
        return ImageEffectGraph.Create(definition, renderer, inputs);
    }

    /// <summary>Creates a color-matrix operation using a supplied or newly created renderer.</summary>
    /// <param name="compiler">The borrowed compiler. Construction allocates no GPU resources.</param>
    /// <param name="matrix">The immutable matrix.</param>
    /// <param name="input">The input graph.</param>
    /// <param name="renderer">The renderer to bind, or null to create the provided implementation.</param>
    /// <returns>The graph constructed by the common effect factory.</returns>
    public static ImageEffectGraph ColorMatrix(this ImageEffectCompiler compiler, ColorMatrix4x5 matrix, ImageEffectGraph input, IImageEffectRenderer<ColorMatrixEffect>? renderer = null)
        => compiler.Effect(new ColorMatrixEffect(matrix), renderer ?? new DefaultColorMatrixEffectRenderer(), input);

    /// <summary>Creates a Gaussian operation using a supplied or newly created renderer.</summary>
    /// <param name="compiler">The borrowed compiler. Construction allocates no GPU resources.</param>
    /// <param name="sigmaX">The finite nonnegative horizontal device-pixel sigma.</param>
    /// <param name="sigmaY">The finite nonnegative vertical device-pixel sigma.</param>
    /// <param name="input">The input graph.</param>
    /// <param name="renderer">The renderer to bind, or null to create the provided implementation.</param>
    /// <returns>The graph constructed by the common effect factory.</returns>
    public static ImageEffectGraph GaussianBlur(this ImageEffectCompiler compiler, float sigmaX, float sigmaY, ImageEffectGraph input, IImageEffectRenderer<GaussianBlurEffect>? renderer = null)
        => compiler.Effect(new GaussianBlurEffect(sigmaX, sigmaY), renderer ?? new DefaultGaussianBlurEffectRenderer(), input);

    /// <summary>Creates an opacity operation using a supplied or newly created renderer.</summary>
    /// <param name="compiler">The borrowed compiler. Construction allocates no GPU resources.</param>
    /// <param name="opacity">A finite multiplier in [0, 1].</param>
    /// <param name="input">The input graph.</param>
    /// <param name="renderer">The renderer to bind, or null to create the provided implementation.</param>
    /// <returns>The graph constructed by the common effect factory.</returns>
    public static ImageEffectGraph Opacity(this ImageEffectCompiler compiler, float opacity, ImageEffectGraph input, IImageEffectRenderer<OpacityEffect>? renderer = null)
        => compiler.Effect(new OpacityEffect(opacity), renderer ?? new DefaultOpacityEffectRenderer(), input);

    /// <summary>Creates a composition operation using a supplied or newly created renderer.</summary>
    /// <param name="compiler">The borrowed compiler. Construction allocates no GPU resources.</param>
    /// <param name="source">The source graph.</param>
    /// <param name="backdrop">The backdrop graph.</param>
    /// <param name="mode">The composition mode.</param>
    /// <param name="sourceOffsetX">The integer horizontal source translation.</param>
    /// <param name="sourceOffsetY">The integer vertical source translation.</param>
    /// <param name="renderer">The renderer to bind, or null to create the provided implementation.</param>
    /// <returns>The graph constructed by the common effect factory.</returns>
    public static ImageEffectGraph Composite(this ImageEffectCompiler compiler, ImageEffectGraph source, ImageEffectGraph backdrop, EffectCompositeMode mode, int sourceOffsetX = 0, int sourceOffsetY = 0, IImageEffectRenderer<CompositeEffect>? renderer = null)
        => compiler.Effect(new CompositeEffect(mode, sourceOffsetX, sourceOffsetY), renderer ?? new DefaultCompositeEffectRenderer(), source, backdrop);
}
