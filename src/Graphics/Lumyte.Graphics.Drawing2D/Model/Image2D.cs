using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.Drawing2D;

/// <summary>A sampled color image borrowing its view, allocation, and sampler.</summary>
public sealed class Image2D
{
    /// <summary>Initializes a new instance of the <see cref="Image2D"/> class.</summary>
    /// <param name="view">A single-mip, single-layer sampled D2 color view.</param>
    /// <param name="sampler">The borrowed sampler.</param>
    /// <param name="alpha">The alpha representation after format decoding.</param>
    public Image2D(IGraphicsTextureView view, IGraphicsSampler sampler, AlphaRepresentation2D alpha = AlphaRepresentation2D.Straight)
    {
        ModelValidation2D.ColorView(view, nameof(view));
        ModelValidation2D.Sampler(sampler, nameof(sampler));
        ModelValidation2D.EnumValue(alpha, nameof(alpha));
        View = view;
        Sampler = sampler;
        Alpha = alpha;
    }

    /// <summary>Gets the alpha representation after format decoding.</summary>
    public AlphaRepresentation2D Alpha { get; }

    /// <summary>Gets the borrowed texture view.</summary>
    public IGraphicsTextureView View { get; }

    /// <summary>Gets the borrowed sampler.</summary>
    public IGraphicsSampler Sampler { get; }
}
