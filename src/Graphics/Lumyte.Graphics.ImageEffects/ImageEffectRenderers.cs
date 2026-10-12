namespace Lumyte.Graphics.ImageEffects;

/// <summary>Stores one renderer per effect operation without runtime capability selection.</summary>
public sealed class ImageEffectRenderers
{
    /// <summary>Initializes a new instance of the <see cref="ImageEffectRenderers"/> class from borrowed renderers.</summary>
    /// <param name="colorMatrix">The matrix renderer.</param>
    /// <param name="gaussianBlur">The blur renderer.</param>
    /// <param name="opacity">The opacity renderer.</param>
    /// <param name="composite">The composite renderer.</param>
    public ImageEffectRenderers(IColorMatrixEffectRenderer colorMatrix, IGaussianBlurEffectRenderer gaussianBlur, IOpacityEffectRenderer opacity, ICompositeEffectRenderer composite)
    {
        ArgumentNullException.ThrowIfNull(colorMatrix);
        ArgumentNullException.ThrowIfNull(gaussianBlur);
        ArgumentNullException.ThrowIfNull(opacity);
        ArgumentNullException.ThrowIfNull(composite);
        ColorMatrix = colorMatrix;
        GaussianBlur = gaussianBlur;
        Opacity = opacity;
        Composite = composite;
    }

    /// <summary>Gets the matrix renderer.</summary>
    public IColorMatrixEffectRenderer ColorMatrix { get; }

    /// <summary>Gets the blur renderer.</summary>
    public IGaussianBlurEffectRenderer GaussianBlur { get; }

    /// <summary>Gets the opacity renderer.</summary>
    public IOpacityEffectRenderer Opacity { get; }

    /// <summary>Gets the composite renderer.</summary>
    public ICompositeEffectRenderer Composite { get; }

    /// <summary>Creates a fresh table and four stateless defaults without allocating GPU resources.</summary>
    /// <returns>The independent renderer table.</returns>
    public static ImageEffectRenderers CreateDefault() => new(new DefaultColorMatrixEffectRenderer(), new DefaultGaussianBlurEffectRenderer(), new DefaultOpacityEffectRenderer(), new DefaultCompositeEffectRenderer());
}
