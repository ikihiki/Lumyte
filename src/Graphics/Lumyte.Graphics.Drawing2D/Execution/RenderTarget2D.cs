using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.Drawing2D;

/// <summary>Describes a borrowed display-linear two-dimensional rendering destination.</summary>
public sealed record RenderTarget2D
{
    /// <summary>Gets the whole single-mip, single-layer Rgba8Srgb or Rgba16Float attachment view.</summary>
    public required IGraphicsTextureView View { get; init; }

    /// <summary>Gets the finite positive logical-to-physical pixel scale.</summary>
    public float PixelScale { get; init; } = 1;

    /// <summary>Gets the initial attachment operation.</summary>
    public AttachmentLoadOp LoadOp { get; init; } = AttachmentLoadOp.Load;

    /// <summary>Gets the straight sRGB color used by Clear.</summary>
    public Color2D ClearColor { get; init; }
}
