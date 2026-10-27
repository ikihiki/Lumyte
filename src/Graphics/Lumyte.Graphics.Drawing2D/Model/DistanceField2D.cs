using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.Drawing2D;

/// <summary>A signed distance field whose positive distances are inside the contour.</summary>
public sealed class DistanceField2D
{
    /// <summary>Initializes a new instance of the <see cref="DistanceField2D"/> class.</summary>
    /// <param name="view">A sampled single-mip, single-layer D2 view, R8Unorm for SDF or Rgba8Unorm for MSDF/MTSDF.</param>
    /// <param name="sampler">The borrowed sampler.</param>
    /// <param name="encoding">The channel interpretation.</param>
    /// <param name="distanceRange">The positive finite multiplier in texel-distance = (sample - 0.5) * range.</param>
    public DistanceField2D(IGraphicsTextureView view, IGraphicsSampler sampler, DistanceFieldEncoding2D encoding, float distanceRange)
    {
        ModelValidation2D.SampledView(view, nameof(view));
        ModelValidation2D.Sampler(sampler, nameof(sampler));
        ModelValidation2D.EnumValue(encoding, nameof(encoding));
        ModelValidation2D.Positive(distanceRange, nameof(distanceRange));
        TextureFormat expected = encoding == DistanceFieldEncoding2D.Sdf ? TextureFormat.R8Unorm : TextureFormat.Rgba8Unorm;
        if (view.Info.Format != expected)
        {
            throw new NotSupportedException($"The distance encoding requires {expected} storage.");
        }

        View = view;
        Sampler = sampler;
        Encoding = encoding;
        DistanceRange = distanceRange;
    }

    /// <summary>Gets the borrowed texture view.</summary>
    public IGraphicsTextureView View { get; }

    /// <summary>Gets the borrowed sampler.</summary>
    public IGraphicsSampler Sampler { get; }

    /// <summary>Gets the signed-distance channel encoding.</summary>
    public DistanceFieldEncoding2D Encoding { get; }

    /// <summary>Gets the finite positive distance range in texels.</summary>
    public float DistanceRange { get; }
}
