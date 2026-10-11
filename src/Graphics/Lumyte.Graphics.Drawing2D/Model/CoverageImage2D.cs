using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.Drawing2D;

/// <summary>A sampled R8Unorm coverage image with no color-space conversion.</summary>
public sealed class CoverageImage2D
{
    /// <summary>Initializes a new instance of the <see cref="CoverageImage2D"/> class.</summary>
    /// <param name="view">A borrowed, single-mip, single-layer sampled D2 R8Unorm view.</param>
    /// <param name="sampler">The borrowed sampler.</param>
    public CoverageImage2D(IGraphicsTextureView view, IGraphicsSampler sampler)
    {
        ModelValidation2D.SampledView(view, nameof(view));
        ModelValidation2D.Sampler(sampler, nameof(sampler));
        if (view.Info.Format != TextureFormat.R8Unorm)
        {
            throw new NotSupportedException("Coverage images require R8Unorm storage.");
        }

        View = view;
        Sampler = sampler;
    }

    /// <summary>Gets the borrowed texture view.</summary>
    public IGraphicsTextureView View { get; }

    /// <summary>Gets the borrowed sampler.</summary>
    public IGraphicsSampler Sampler { get; }
}
